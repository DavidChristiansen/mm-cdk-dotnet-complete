using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;

namespace Cdk
{
    internal class XRayStack : Stack
    {
        public XRayStack(Construct parent, string id) : base(parent, id)
        {
            var table = new Table(this, "Table", new TableProps()
            {
                TableName = "MysfitsQuestionsTable",
                PartitionKey = new Attribute
                {
                    Name = "QuestionId",
                    Type = AttributeType.STRING
                },
                Stream = StreamViewType.NEW_IMAGE
            });

            var postQuestionLambdaFunctionPolicyStmDDB = new PolicyStatement();
            postQuestionLambdaFunctionPolicyStmDDB.AddActions("dynamodb:PutItem");
            postQuestionLambdaFunctionPolicyStmDDB.AddResources(table.TableArn);

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

            var mysfitsPostQuestion = new Function(this, "PostQuestionFunction", new FunctionProps
            {
                Handler = "mysfitsPostQuestion.postQuestion",
                Runtime = Runtime.PYTHON_3_6,
                Description =
                    "A microservice Lambda function that receives a new question submitted to the MythicalMysfits website from a user and inserts it into a DynamoDB database table.",
                MemorySize = 128,
                Code = Code.FromAsset("../../lambda-questions/PostQuestionsService"),
                Timeout = Duration.Seconds(30),
                InitialPolicy = new[]
                {
                    postQuestionLambdaFunctionPolicyStmDDB,
                    LambdaFunctionPolicyStmXRay
                },
                Tracing = Tracing.ACTIVE
            });

            var topic = new Topic(this, "Topic", new TopicProps
            {
                DisplayName = "MythicalMysfitsQuestionsTopic",
                TopicName = "MythicalMysfitsQuestionsTopic"
            });
            topic.AddSubscription(new EmailSubscription("REPLACE@EMAIL_ADDRESS"));

            var postQuestionLambdaFunctionPolicyStmSNS = new PolicyStatement();
            postQuestionLambdaFunctionPolicyStmSNS.AddActions("sns:Publish");
            postQuestionLambdaFunctionPolicyStmSNS.AddResources(topic.TopicArn);

            var mysfitsProcessQuestionStream = new Function(this, "ProcessQuestionStreamFunction", new FunctionProps
            {
                Handler = "mysfitsProcessStream.processStream",
                Runtime = Runtime.PYTHON_3_6,
                Description =
                    "An AWS Lambda function that will process all new questions posted to mythical mysfits" +
                    " and notify the site administrator of the question that was asked.",
                MemorySize = 128,
                Code = Code.FromAsset("../../lambda-questions/ProcessQuestionsStream"),
                Timeout = Duration.Seconds(30),
                InitialPolicy = new[]
                {
                    postQuestionLambdaFunctionPolicyStmSNS,
                    LambdaFunctionPolicyStmXRay
                },
                Tracing = Tracing.ACTIVE,
                Environment = new Dictionary<string, string>()
                {
                    {"SNS_TOPIC_ARN", topic.TopicArn}
                },
                Events = new IEventSource[]
                {
                    new DynamoEventSource(table, new DynamoEventSourceProps
                    {
                        StartingPosition = StartingPosition.TRIM_HORIZON,
                        BatchSize = 1
                    })
                }
            });

            var questionsApiRole = new Role(this, "QuestionsApiRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("apigateway.amazonaws.com")
            });

            var apiPolicy = new PolicyStatement();
            apiPolicy.AddActions("lambda:InvokeFunction");
            apiPolicy.AddResources(mysfitsPostQuestion.FunctionArn);
            new Policy(this, "QuestionsApiPolicy", new PolicyProps
            {
                PolicyName = "questions_api_policy",
                Statements = new[]
                {
                    apiPolicy
                },
                Roles = new IRole[]
                {
                    questionsApiRole
                }
            });

            var questionsIntegration = new LambdaIntegration(mysfitsPostQuestion, new LambdaIntegrationOptions
            {
                CredentialsRole = questionsApiRole,
                IntegrationResponses = new IntegrationResponse[]
                {
                    new IntegrationResponse()
                    {
                        StatusCode = "200",
                        ResponseTemplates = new Dictionary<string, string>
                        {
                            {"application/json", "{\"status\":\"OK\"}"}
                        }
                    }
                }
            });

            var api = new LambdaRestApi(this, "APIEndpoint", new LambdaRestApiProps
            {
                Handler = mysfitsPostQuestion,
                RestApiName = "Questions API Service",
                DeployOptions = new StageOptions
                {
                    TracingEnabled = true
                },
                Proxy = false
            });

            var questionsMethod = api.Root.AddResource("questions");
            questionsMethod.AddMethod("POST", questionsIntegration, new MethodOptions
            {
                MethodResponses = new MethodResponse[]
                {
                    new MethodResponse
                    {
                        StatusCode = "200",
                        ResponseParameters = new Dictionary<string, bool>()
                        {
                            {"method.response.header.Access-Control-Allow-Headers", true},
                            {"method.response.header.Access-Control-Allow-Methods", true},
                            {"method.response.header.Access-Control-Allow-Origin", true},
                        }
                    }
                },
                AuthorizationType = AuthorizationType.NONE
            });

            questionsMethod.AddMethod("OPTIONS", new MockIntegration(new IntegrationOptions
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
                }
            );
        }
    }
}