using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cdk
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App(null);
            new CdkStack(app, "CdkStack", new StackProps());
            app.Synth();
        }
    }
}
