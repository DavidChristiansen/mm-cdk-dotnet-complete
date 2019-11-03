using Amazon.CDK;

namespace Cdk
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App(null);
            var developerToolStack = new DeveloperToolsStack(app, "MythicalMysfits-DeveloperTools");
            new WebApplicationStack(app, "MythicalMysfits-WebApplication");
            var networkStack = new NetworkStack(app, "MythicalMysfits-Network");
            var ecrStack = new EcrStack(app, "MythicalMysfits-ECR");
            var ecsStack = new EcsStack(app, "MythicalMysfits-ECS", new EcsStackProps
            {
                Vpc = networkStack.vpc,
                ecrRepository = ecrStack.ecrRepository
            });
            new CiCdStack(app, "MythicalMysfits", new CiCdStackProps
            {
                ecrRepository = ecrStack.ecrRepository,
                ecsService = ecsStack.ecsService.Service,
                apiRepositoryArn = developerToolStack.apiRepository.RepositoryArn
            });
            var dynamoDbStack = new DynamoDbStack(app, "MythicalMysfits-DynamoDB", new DynamoDbStackProps {
              Vpc= networkStack.vpc,
              fargateService= ecsStack.ecsService.Service
            });
            var cognito = new CognitoStack(app, "MythicalMysfits-Cognito");
            var apiGateway = new APIGatewayStack(app, "MythicalMysfits-APIGateway", new APIGatewayStackProps {
              userPoolId= cognito.userPool.UserPoolId,
              loadBalancerArn= ecsStack.ecsService.LoadBalancer.LoadBalancerArn,
              loadBalancerDnsName= ecsStack.ecsService.LoadBalancer.LoadBalancerDnsName
            });
            new KinesisFirehoseStack(app, "MythicalMysfits-KinesisFirehose", new KinesisFirehoseStackProps {
              TableArn= dynamoDbStack.table.TableArn,
              APIid= apiGateway.apiId
            });
            new XRayStack(app, "MythicalMysfits-XRay");

            app.Synth();
        }
    }
}
