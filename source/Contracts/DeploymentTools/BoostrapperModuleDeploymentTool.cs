﻿using System;
using System.Collections.Generic;
using Octopus.CoreUtilities;

namespace Octopus.Sashimi.Contracts.DeploymentTools
{
    public class BoostrapperModuleDeploymentTool : IDeploymentTool
    {
        readonly IReadOnlyList<string> modulePaths;

        public BoostrapperModuleDeploymentTool(string id, IReadOnlyList<string> modulePaths, params string[] supportedPlatforms)
        {
            this.modulePaths = modulePaths;
            Id = id;
            SupportedPlatforms = supportedPlatforms ?? new string[0];
        }

        public string Id { get; }
        public Maybe<string> SubFolder => Maybe<string>.None;
        public bool AddToPath => false;
        public Maybe<string> ToolPathVariableToSet => Maybe<string>.None;
        public string[] SupportedPlatforms { get; }

        public Maybe<DeploymentToolPackage> GetCompatiblePackage(string platform)
            => platform == null ? new DeploymentToolPackage(this, Id, modulePaths).AsSome() : Maybe<DeploymentToolPackage>.None;
    }
}