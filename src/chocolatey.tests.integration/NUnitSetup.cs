﻿// Copyright © 2011 - Present RealDimensions Software, LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
// You may obtain a copy of the License at
//
// 	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace chocolatey.tests.integration
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using SimpleInjector;
    using chocolatey.infrastructure.app;
    using chocolatey.infrastructure.app.builders;
    using chocolatey.infrastructure.app.commands;
    using chocolatey.infrastructure.app.configuration;
    using chocolatey.infrastructure.filesystem;
    using chocolatey.infrastructure.platforms;
    using chocolatey.infrastructure.registration;
    using chocolatey.infrastructure.services;

    // ReSharper disable InconsistentNaming

    [SetUpFixture]
    public class NUnitSetup : tests.NUnitSetup
    {
        public static Container Container { get; set; }

        public override void BeforeEverything()
        {
            Container = SimpleInjectorContainer.Container;
            fix_application_parameter_variables(Container);
            var config = Container.GetInstance<ChocolateyConfiguration>();
            config.Information.PlatformType = PlatformType.Windows;
            //config.Information.IsInteractive = false; 
            //config.PromptForConfirmation = false;

            var force = config.Force;
            config.Force = true;
            build_packages(Container, config);
            unpack_self(Container,config);
            config.Force = force;

            base.BeforeEverything();

            ConfigurationBuilder.set_up_configuration(new List<string>(), config, Container.GetInstance<IFileSystem>(), Container.GetInstance<IXmlService>(), null);
        }

        /// <summary>
        /// Most of the application parameters are already set by runtime and are readonly values.
        ///  They need to be updated, so we can do that with reflection.
        /// </summary>
        private static void fix_application_parameter_variables(Container container)
        {
            var fileSystem = container.GetInstance<IFileSystem>();

            var applicationLocation = fileSystem.get_directory_name(fileSystem.get_current_assembly_path());

            var field = typeof (ApplicationParameters).GetField("InstallLocation");
            field.SetValue(null, applicationLocation);

            field = typeof (ApplicationParameters).GetField("LicenseFileLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "license", "chocolatey.license.xml"));

            field = typeof (ApplicationParameters).GetField("LoggingLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "logs"));

            field = typeof (ApplicationParameters).GetField("GlobalConfigFileLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "config", "chocolatey.config"));

            field = typeof (ApplicationParameters).GetField("PackagesLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "lib"));

            field = typeof (ApplicationParameters).GetField("PackageFailuresLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "lib-bad"));

            field = typeof(ApplicationParameters).GetField("PackageBackupLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "lib-bkp"));

            field = typeof (ApplicationParameters).GetField("ShimsLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, "bin"));

            field = typeof (ApplicationParameters).GetField("ChocolateyPackageInfoStoreLocation");
            field.SetValue(null, fileSystem.combine_paths(ApplicationParameters.InstallLocation, ".chocolatey"));

            // we need to speed up specs a bit, so only try filesystem locking operations twice
            field = fileSystem.GetType().GetField("TIMES_TO_TRY_OPERATION", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(fileSystem, 2);
            }
        }

        private void unpack_self(Container container, ChocolateyConfiguration config)
        {
           var unpackCommand = container.GetInstance<ChocolateyUnpackSelfCommand>();
            unpackCommand.run(config);
        }

        private void build_packages(Container container, ChocolateyConfiguration config)
        {
            var input = config.Input;

            var fileSystem = container.GetInstance<IFileSystem>();
            var contextDir = fileSystem.combine_paths(fileSystem.get_directory_name(fileSystem.get_current_assembly_path()), "context");
            
            // short-circuit building packages if they are already there.
            if (fileSystem.get_files(contextDir, "*.nupkg").Any())
            {
                System.Console.WriteLine("Packages have already been built. Skipping... - If you need to rebuild packages, delete all nupkg files in {0}.".format_with(contextDir));
                return;
            }

            var files = fileSystem.get_files(contextDir, "*.nuspec", SearchOption.AllDirectories);
            
            var command = container.GetInstance<ChocolateyPackCommand>();   
            foreach (var file in files.or_empty_list_if_null())
            {
                config.Input = file;
                System.Console.WriteLine("Building {0}".format_with(file));
                command.run(config);
            }

            System.Console.WriteLine("Moving all nupkgs in {0} to context directory.".format_with(fileSystem.get_current_directory()));
            var nupkgs = fileSystem.get_files(fileSystem.get_current_directory(), "*.nupkg");

            foreach (var nupkg in nupkgs.or_empty_list_if_null())
            {
                fileSystem.copy_file(nupkg,fileSystem.combine_paths(contextDir,fileSystem.get_file_name(nupkg)),overwriteExisting:true);
                fileSystem.delete_file(nupkg);
            }
            
            config.Input = input;
        }
    }

    // ReSharper restore InconsistentNaming
}
