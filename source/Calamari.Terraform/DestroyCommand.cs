﻿using Calamari.Commands.Support;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;

namespace Calamari.Terraform
{
    [Command("destroy-terraform", Description = "Destroys Terraform resources")]
    public class DestroyCommand : TerraformCommand
    {
        public DestroyCommand(ILog log, IVariables variables, ICalamariFileSystem fileSystem, ICommandLineRunner commandLineRunner)
            : base(log, variables, fileSystem, new DestroyTerraformConvention(log, fileSystem, commandLineRunner))
        {
        }
    }
}