using Amazon.CDK;
using Amazon.CDK.AWS.ECR;

namespace Cdk
{
    internal class EcrStack : Stack
    {
        public Repository ecrRepository { get; }

        public EcrStack(Construct parent, string id) : base(parent, id)
        {
            this.ecrRepository = new Repository(this, "Repository", new RepositoryProps
            {
                RepositoryName = "mythicalmysfits/service"
            });
        }
    }
}