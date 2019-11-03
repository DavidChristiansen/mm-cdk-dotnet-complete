using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KinesisFirehose;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;

namespace Cdk
{
    internal class KinesisFirehoseStack : Stack
    {
        public KinesisFirehoseStack(Construct parent, string id, KinesisFirehoseStackProps props) : base(parent, id,
            props)
        {
            var clicksDestinationBucket = new Bucket(this, "Bucket", new BucketProps
            {
                Versioned = true
            });

            var firehoseDeliveryRole = new Role(this, "FirehoseDeliveryRole", new RoleProps
            {
                RoleName = "FirehoseDeliveryRole",
                AssumedBy = new ServicePrincipal("firehose.amazonaws.com"),
                ExternalIds = new string[]
                {
                    Aws.ACCOUNT_ID
                }
            });

            var firehoseDeliveryPolicyS3Stm = new PolicyStatement();
            firehoseDeliveryPolicyS3Stm.AddActions("s3:AbortMultipartUpload",
                "s3:GetBucketLocation",
                "s3:GetObject",
                "s3:ListBucket",
                "s3:ListBucketMultipartUploads",
                "s3:PutObject");
            firehoseDeliveryPolicyS3Stm.AddResources(clicksDestinationBucket.BucketArn);
            firehoseDeliveryPolicyS3Stm.AddResources(clicksDestinationBucket.ArnForObjects("*"));

            var lambdaFunctionPolicy = new PolicyStatement();
            lambdaFunctionPolicy.AddActions("dynamodb:GetItem");
            lambdaFunctionPolicy.AddResources(props.TableArn);
            var LambdaFunctionPolicyStmXRay = new PolicyStatement();
            LambdaFunctionPolicyStmXRay.AddActions(
                //  Allows the Lambda function to interact with X-Ray
                "xray:PutTraceSegments",
                "xray:PutTelemetryRecords",
                "xray:GetSamplingRules",
                "xray:GetSamplingTargets",
                "xray:GetSamplingStatisticSummaries"
            );
            LambdaFunctionPolicyStmXRay.AddAllResources();
            var mysfitsClicksProcessor = new Function(this, "Function", new FunctionProps
            {
                Handler = "streaming_lambda::streaming_lambda.function::FunctionHandlerAsync",
                Runtime = Runtime.DOTNET_CORE_2_1,
                Description = "An Amazon Kinesis Firehose stream processor that enriches click records" +
                              " to not just include a mysfitId, but also other attributes that can be analyzed later.",
                MemorySize = 128,
                Code = Code.FromAsset("../lambda/stream/bin/Debug/netcoreapp2.1/Publish"),
                Timeout = Duration.Seconds(30),
                Tracing = Tracing.ACTIVE,
                InitialPolicy = new PolicyStatement[]
                {
                    lambdaFunctionPolicy,
                    LambdaFunctionPolicyStmXRay
                },
                Environment = new Dictionary<string, string>()
                {
                    {
                        "mysfits_api_url",
                        string.Format($"https://${props.APIid}.execute-api.{Amazon.CDK.Aws.REGION}.amazonaws.com/prod/")
                    }
                }
            });

            var firehoseDeliveryPolicyLambdaStm = new PolicyStatement();
            firehoseDeliveryPolicyLambdaStm.AddActions("lambda:InvokeFunction");
            firehoseDeliveryPolicyLambdaStm.AddResources(mysfitsClicksProcessor.FunctionArn);
            firehoseDeliveryRole.AddToPolicy(firehoseDeliveryPolicyS3Stm);
            firehoseDeliveryRole.AddToPolicy(firehoseDeliveryPolicyLambdaStm);

            var mysfitsFireHoseToS3 = new CfnDeliveryStream(this, "DeliveryStream", new CfnDeliveryStreamProps
            {
                ExtendedS3DestinationConfiguration = new CfnDeliveryStream.ExtendedS3DestinationConfigurationProperty()
                {
                    BucketArn = clicksDestinationBucket.BucketArn,
                    BufferingHints = new CfnDeliveryStream.BufferingHintsProperty
                    {
                        IntervalInSeconds = 60,
                        SizeInMBs = 50
                    },
                    CompressionFormat = "UNCOMPRESSED",
                    Prefix = "firehose/",
                    RoleArn = firehoseDeliveryRole.RoleArn,
                    ProcessingConfiguration = new CfnDeliveryStream.ProcessingConfigurationProperty
                    {
                        Enabled = true,
                        Processors = new CfnDeliveryStream.ProcessorProperty[]
                        {
                            new CfnDeliveryStream.ProcessorProperty()
                            {
                                Type = "Lambda",
                                Parameters = new CfnDeliveryStream.ProcessorParameterProperty
                                {
                                    ParameterName = "LambdaArn",
                                    ParameterValue = mysfitsClicksProcessor.FunctionArn
                                }
                            }
                        }
                    }
                }
            });

            new CfnPermission(this, "Permission", new CfnPermissionProps
            {
                Action = "lambda:InvokeFunction",
                FunctionName = mysfitsClicksProcessor.FunctionArn,
                Principal = "firehose.amazonaws.com",
                SourceAccount = Amazon.CDK.Aws.ACCOUNT_ID,
                SourceArn = mysfitsFireHoseToS3.AttrArn
            });

            var clickProcessingApiRole = new Role(this, "ClickProcessingApiRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("apigateway.amazonaws.com")
            });

            var apiPolicy = new PolicyStatement();
            apiPolicy.AddActions("firehose:PutRecord");
            apiPolicy.AddResources(mysfitsFireHoseToS3.AttrArn);
            new Policy(this, "ClickProcessingApiPolicy", new PolicyProps
            {
                PolicyName = "api_gateway_firehose_proxy_role",
                Statements = new PolicyStatement[]
                {
                    apiPolicy
                },
                Roles = new[] {clickProcessingApiRole}
            });

            var api = new RestApi(this, "APIEndpoint", new RestApiProps
            {
                RestApiName = "ClickProcessing API Service",
                CloudWatchRole = false,
                EndpointTypes = new EndpointType[]
                {
                    EndpointType.REGIONAL
                }
            });

            var clicks = api.Root.AddResource("clicks");

            clicks.AddMethod("PUT", new AwsIntegration(new AwsIntegrationProps
                {
                    Service = "firehose",
                    IntegrationHttpMethod = "POST",
                    Action = "PutRecord",
                    Options = new IntegrationOptions
                    {
                        ConnectionType = ConnectionType.INTERNET,
                        CredentialsRole = clickProcessingApiRole,
                        IntegrationResponses = new IntegrationResponse[]
                        {
                            new IntegrationResponse()
                            {
                                StatusCode = "200",
                                ResponseTemplates =
                                {
                                    {"application/json", "{\"status\":\"OK\"}"}
                                },
                                ResponseParameters =
                                {
                                    {"method.response.header.Access-Control-Allow-Headers", "'Content-Type'"},
                                    {"method.response.header.Access-Control-Allow-Methods", "'OPTIONS,PUT'"},
                                    {"method.response.header.Access-Control-Allow-Origin", "'*'"}
                                }
                            }
                        },
                        RequestParameters =
                        {
                            {"integration.request.header.Content-Type", "'application/x-amz-json-1.1'"}
                        },
                        RequestTemplates =
                        {
                            {
                                "application/json",
                                "{\"DeliveryStreamName\":\"" + mysfitsFireHoseToS3.Ref +
                                "\", \"Record\": { \"Data\": \"$util.base64Encode($input.json('$'))\"}"
                            }
                        }
                    }
                }),
                new MethodOptions
                {
                    MethodResponses = new MethodResponse[]
                    {
                        new MethodResponse
                        {
                            StatusCode = "200",
                            ResponseParameters =
                            {
                                {"method.response.header.Access-Control-Allow-Headers", true},
                                {"method.response.header.Access-Control-Allow-Methods", true},
                                {"method.response.header.Access-Control-Allow-Origin", true}
                            }
                        }
                    }
                });

            clicks.AddMethod("OPTIONS", new MockIntegration(new IntegrationOptions
                {
                    IntegrationResponses = new IntegrationResponse[]
                    {
                        new IntegrationResponse
                        {
                            StatusCode = "200",
                            ResponseParameters = new Dictionary<string, string>
                            {
                                {
                                    "method.response.header.Access-Control-Allow-Headers",
                                    "'Content-Type,X-Amz-Date,Authorization,X-Api-Key,X-Amz-Security-Token,X-Amz-User-Agent'"
                                },
                                {"method.response.header.Access-Control-Allow-Origin", "'*'"},
                                {"method.response.header.Access-Control-Allow-Credentials", "'false'"},
                                {"method.response.header.Access-Control-Allow-Methods", "'OPTIONS,GET,PUT,POST,DELETE'"}
                            }
                        }
                    },
                    PassthroughBehavior = PassthroughBehavior.NEVER,
                    RequestTemplates = new Dictionary<string, string>
                    {
                        {"application/json", "{\"statusCode\": 200}"}
                    }
                }),
                new MethodOptions
                {
                    MethodResponses = new MethodResponse[]
                    {
                        new MethodResponse
                        {
                            StatusCode = "200",
                            ResponseParameters = new Dictionary<string, bool>
                            {
                                {"method.response.header.Access-Control-Allow-Headers", true},
                                {"method.response.header.Access-Control-Allow-Methods", true},
                                {"method.response.header.Access-Control-Allow-Credentials", true},
                                {"method.response.header.Access-Control-Allow-Origin", true}
                            }
                        }
                    }
                });
        }
    }

    internal class KinesisFirehoseStackProps : StackProps
    {
        public string TableArn { get; internal set; }
        public string APIid { get; internal set; }
    }
}