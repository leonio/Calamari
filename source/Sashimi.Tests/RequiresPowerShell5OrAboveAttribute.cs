using System;
using Calamari.Common.Plumbing.Extensions;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Sashimi.AzureResourceGroup.Tests
{
    public class RequiresPowerShell5OrAboveAttribute : NUnitAttribute, IApplyToTest
    {
        public void ApplyToTest(Test test)
        {
            if (ScriptingEnvironment.SafelyGetPowerShellVersion().Major < 5)
            {
                test.RunState = RunState.Skipped;
                test.Properties.Set(PropertyNames.SkipReason, "This test requires PowerShell 5 or newer.");
            }
        }
    }
}