﻿using Calamari.Common.Plumbing.FileSystem;
using Calamari.Common.Plumbing.Logging;

namespace Calamari.Common.Features.StructuredVariables
{
    public static class FileFormatVariableReplacers
    {
        // TODO: Once we have a good DI solution this can be removed.
        public static IFileFormatVariableReplacer[] BuildAllReplacers(ICalamariFileSystem fileSystem, ILog log)
        {
            return new IFileFormatVariableReplacer[]
            {
                new JsonFormatVariableReplacer(fileSystem, log),
                new YamlFormatVariableReplacer()
            };
        }
    }
}