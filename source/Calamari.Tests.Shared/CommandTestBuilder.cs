﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Calamari.Common;
using Calamari.Common.Commands;
using Calamari.Common.Plumbing.FileSystem;
using Calamari.Common.Plumbing.Logging;
using Calamari.Common.Plumbing.Pipeline;
using Calamari.Common.Plumbing.Variables;
using Calamari.Tests.Shared.Helpers;
using Calamari.Tests.Shared.LogParser;
using FluentAssertions;
using KnownVariables = Calamari.Common.Plumbing.Variables.KnownVariables;

namespace Calamari.Tests.Shared
{
    public static class CommandTestBuilder
    {
        public static CommandTestBuilder<TCalamari> CreateAsync<TCalamari>(string command)
            where TCalamari : CalamariFlavourProgramAsync
        {
            return new CommandTestBuilder<TCalamari>(command);
        }

        public static CommandTestBuilder<TCalamari> CreateAsync<TCommand, TCalamari>()
            where TCalamari : CalamariFlavourProgramAsync
            where TCommand : PipelineCommand
        {
            return new CommandTestBuilder<TCalamari>(typeof(TCommand).GetCustomAttribute<CommandAttribute>().Name);
        }

        public static CommandTestBuilder<TCalamari> Create<TCalamari>(string command)
            where TCalamari : CalamariFlavourProgram
        {
            return new CommandTestBuilder<TCalamari>(command);
        }

        public static CommandTestBuilder<TCalamari> Create<TCommand, TCalamari>()
            where TCalamari : CalamariFlavourProgram
            where TCommand : ICommand
        {
            return new CommandTestBuilder<TCalamari>(typeof(TCommand).GetCustomAttribute<CommandAttribute>().Name);
        }

        public static CommandTestBuilderContext WithFilesToCopy(this CommandTestBuilderContext context, string path)
        {
            if (File.Exists(path))
            {
                context.Variables.Add(KnownVariables.OriginalPackageDirectoryPath, Path.GetDirectoryName(path));
            }
            else
            {
                context.Variables.Add(KnownVariables.OriginalPackageDirectoryPath, path);
            }

            context.Variables.Add("Octopus.Action.Package.PackageId", path);
            context.Variables.Add("Octopus.Action.Package.FeedId", "FeedId");

            return context;
        }

        public static CommandTestBuilderContext WithPackage(this CommandTestBuilderContext context, string packagePath)
        {
            context.Variables.Add(KnownVariables.OriginalPackageDirectoryPath, Path.GetDirectoryName(packagePath));
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(packagePath);
            var fileNameChunks = fileNameWithoutExtension.Split('.').Reverse().ToArray();
            string? packageVersion = null;
            var idx = 0;
            for (; idx < fileNameChunks.Length; idx++)
            {
                var fileNameChunk = fileNameChunks[idx];

                if (Int32.TryParse(fileNameChunk, out _))
                {
                    packageVersion = packageVersion == null
                        ? fileNameChunk
                        : $"{fileNameChunk}.{packageVersion}";

                    continue;
                }
                break;
            }

            string? packageId = null;
            for (; idx < fileNameChunks.Length; idx++)
            {
                var fileNameChunk = fileNameChunks[idx];

                packageId = packageId == null
                    ? fileNameChunk
                    : $"{fileNameChunk}.{packageId}";
            }

            context.Variables.Add(TentacleVariables.CurrentDeployment.PackageFilePath, packagePath);
            context.Variables.Add("Octopus.Action.Package.PackageId", packageId);
            context.Variables.Add("Octopus.Action.Package.PackageVersion", packageVersion);
            context.Variables.Add("Octopus.Action.Package.FeedId", "FeedId");

            return context;
        }
    }

    public class CommandTestBuilder<TCalamariProgram>
    {
        readonly string command;
        readonly List<Action<CommandTestBuilderContext>> arrangeActions;
        Action<TestCalamariCommandResult>? assertAction;

        internal CommandTestBuilder(string command)
        {
            this.command = command;
            arrangeActions = new List<Action<CommandTestBuilderContext>>();
        }

        public CommandTestBuilder<TCalamariProgram> WithArrange(Action<CommandTestBuilderContext> arrange)
        {
            arrangeActions.Add(arrange);
            return this;
        }

        public CommandTestBuilder<TCalamariProgram> WithAssert(Action<TestCalamariCommandResult> assert)
        {
            assertAction = assert;
            return this;
        }

        public async Task<TestCalamariCommandResult> Execute(bool assertWasSuccess = true)
        {
            var context = new CommandTestBuilderContext();

            List<string> GetArgs(string workingPath)
            {
                var args = new List<string> {command};

                var varPath = Path.Combine(workingPath, "variables.json");

                context.Variables.Save(varPath);
                args.Add($"--variables={varPath}");

                return args;
            }

            void Copy(string sourcePath, string destinationPath)
            {
                foreach (var dirPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dirPath.Replace(sourcePath, destinationPath));
                }

                foreach (var newPath in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(newPath, newPath.Replace(sourcePath, destinationPath), true);
                }
            }

            void CopyFilesToWorkingFolder(string workingPath)
            {
                foreach (var (filename, contents) in context.Files)
                {
                    using var fileStream = File.Create(Path.Combine(workingPath, filename!));
                    contents.Seek(0, SeekOrigin.Begin);
                    contents.CopyTo(fileStream);
                }

                if (!context.withStagedPackageArgument)
                {
                    var packageId = context.Variables.GetRaw("Octopus.Action.Package.PackageId");
                    if (File.Exists(packageId))
                    {
                        var fileName = new FileInfo(packageId).Name;
                        File.Copy(packageId, Path.Combine(workingPath, fileName));
                    }
                    else if (Directory.Exists(packageId))
                    {
                        Copy(packageId, workingPath);
                    }
                }
            }

            async Task<TestCalamariCommandResult> ExecuteActionHandler(List<string> args)
            {
                var inMemoryLog = new InMemoryLog();
                var constructor = typeof(TCalamariProgram).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] {typeof(ILog)}, new ParameterModifier[0]);
                if (constructor == null)
                {
                    throw new Exception(
                        $"{typeof(TCalamariProgram).Name} doesn't seem to have a `public {typeof(TCalamariProgram)}({nameof(ILog)})` constructor.");
                }

                var instance = (TCalamariProgram) constructor.Invoke(new object?[]
                {
                    inMemoryLog
                });

                var methodInfo =
                    typeof(TCalamariProgram).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic);
                if (methodInfo == null)
                {
                    throw new Exception($"{typeof(TCalamariProgram).Name}.Run method was not found.");
                }

                int exitCode;
                if (methodInfo.ReturnType.IsGenericType)
                {
                    exitCode = await (Task<int>) methodInfo.Invoke(instance, new object?[] {args.ToArray()});
                }
                else
                {
                    exitCode = (int) methodInfo.Invoke(instance, new object?[] {args.ToArray()});
                }
                var serverInMemoryLog = new ServerInMemoryLog();

                var outputFilter = new ScriptOutputFilter(serverInMemoryLog);
                foreach (var text in inMemoryLog.StandardError)
                {
                    outputFilter.Write(ProcessOutputSource.StdErr, text);
                }

                foreach (var text in inMemoryLog.StandardOut)
                {
                    outputFilter.Write(ProcessOutputSource.StdOut, text);
                }

                return new TestCalamariCommandResult(exitCode,
                    outputFilter.TestOutputVariables, outputFilter.Actions,
                    outputFilter.ServiceMessages, outputFilter.ResultMessage, outputFilter.Artifacts,
                    serverInMemoryLog.ToString());
            }

            foreach (var arrangeAction in arrangeActions)
            {
                arrangeAction.Invoke(context);
            }

            TestCalamariCommandResult result;

            using (var working = TemporaryDirectory.Create())
            {
                var workingPath = working.DirectoryPath;

                //HACK: set the working directory, we will need to modify Calamari to not depend on this
                var originalWorkingDirectory = Environment.CurrentDirectory;
                try
                {
                    Environment.CurrentDirectory = workingPath;

                    var args = GetArgs(workingPath);

                    CopyFilesToWorkingFolder(workingPath);

                    result = await ExecuteActionHandler(args);
                }
                finally
                {
                    Environment.CurrentDirectory = originalWorkingDirectory;
                }
            }

            if (assertWasSuccess)
            {
                result.WasSuccessful.Should().BeTrue($"{command} execute result was unsuccessful");
            }
            assertAction?.Invoke(result);

            return result;
        }
    }
}