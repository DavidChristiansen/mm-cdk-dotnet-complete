using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;

namespace Cdk
{
    internal class CognitoStack : Stack
    {
        public UserPool userPool { get; }
        public UserPoolClient userPoolClient { get; }

        public CognitoStack(Construct parent, string id) : base(parent, id)
        {
            this.userPool = new UserPool(this, "UserPool", new UserPoolProps
            {
                UserPoolName = "MysfitsUserPool",
                AutoVerifiedAttributes = new UserPoolAttribute[]
                {
                    UserPoolAttribute.EMAIL
                }
            });

            this.userPoolClient = new UserPoolClient(this, "UserPoolClient", new UserPoolClientProps
            {
                UserPool = this.userPool,
                UserPoolClientName = "MysfitsUserPoolClient"
            });
        }
    }
}