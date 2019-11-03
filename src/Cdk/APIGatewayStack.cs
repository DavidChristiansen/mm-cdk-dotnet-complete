using Amazon.CDK;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.APIGateway;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.RegularExpressions;
using static Amazon.CDK.AWS.APIGateway.CfnRestApi;

namespace Cdk
{
    internal class APIGatewayStack : Stack
    {
        public string apiId { get; }

        public APIGatewayStack(Construct parent, string id, APIGatewayStackProps props) : base(parent, id, props)
        {
            var nlb = NetworkLoadBalancer.FromNetworkLoadBalancerAttributes(this, "NLB",
                new NetworkLoadBalancerAttributes
                {
                    LoadBalancerArn = props.loadBalancerArn
                });
            var vpcLink = new VpcLink(this, "VPCLink", new VpcLinkProps
            {
                Description = "VPC Link for our  REST API",
                VpcLinkName = "MysfitsApiVpcLink",
                Targets = new INetworkLoadBalancer[]
                {
                    nlb
                }
            });

            var schema = this.generateSwaggerSpec(props.loadBalancerDnsName, props.userPoolId, vpcLink);
            var jsonSchema = JObject.Parse(schema);
            var api = new CfnRestApi(this, "Schema", new CfnRestApiProps
            {
                Name = "MysfitsApi",
                Body = jsonSchema,
                FailOnWarnings = true,
                EndpointConfiguration = new EndpointConfigurationProperty
                {
                    Types = new[]
                    {
                        "REGIONAL"
                    }
                }
            });

            new CfnDeployment(this, "Prod", new CfnDeploymentProps
            {
                RestApiId = api.Ref,
                StageName = "prod"
            });
            new CfnOutput(this, "APIID", new CfnOutputProps
            {
                Value = api.Ref,
                Description = "API Gateway ID"
            });
            this.apiId = api.Ref;
        }

        private string generateSwaggerSpec(string dnsName, string userPoolId, VpcLink vpcLink)
        {
            string currentPath = Directory.GetCurrentDirectory();
            var schemaFilePath = Path.Combine(currentPath, "../api-swagger.json");
            var apiSchema = File.ReadAllText(schemaFilePath);
            var schema = Regex.Replace(apiSchema, @"\REPLACE_ME_REGION\b", Amazon.CDK.Aws.REGION);
            schema = Regex.Replace(schema, @"\REPLACE_ME_ACCOUNT_ID\b", Amazon.CDK.Aws.ACCOUNT_ID);
            schema = Regex.Replace(schema, @"\REPLACE_ME_COGNITO_USER_POOL_ID\b", userPoolId);
            schema = Regex.Replace(schema, @"\REPLACE_ME_VPC_LINK_ID\b", vpcLink.VpcLinkId);
            schema = Regex.Replace(schema, @"\REPLACE_ME_NLB_DNS\b", dnsName);
            return schema;
        }
    }

    public class APIGatewayStackProps : StackProps
    {
        public string userPoolId { get; internal set; }
        public string loadBalancerArn { get; internal set; }
        public string loadBalancerDnsName { get; internal set; }
    }
}