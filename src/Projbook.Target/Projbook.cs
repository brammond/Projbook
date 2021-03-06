﻿using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Projbook.Core;
using Projbook.Core.Model;
using Projbook.Core.Model.Configuration;
using System;
using System.IO;

namespace Projbook.Target
{
    /// <summary>
    /// Define MSBuild task trigerring documentation generation.
    /// </summary>
    public class Projbook : Task
    {
        /// <summary>
        /// The project path.
        /// </summary>
        [Required]
        public string ProjectPath { get; set; }

        /// <summary>
        /// The configuration file.
        /// </summary>
        [Required]
        public string ConfigurationFile { get; set; }

        /// <summary>
        /// The output directory.
        /// </summary>
        [Required]
        public string OutputDirectory { get; set; }

        /// <summary>
        /// The WkhtmlToPdf location.
        /// </summary>
        [Required]
        public string WkhtmlToPdfLocation { get; set; }

        /// <summary>
        /// Trigger task execution.
        /// </summary>
        /// <returns>True if the task succeeded.</returns>
        public override bool Execute()
        {
            // Load configuration
            ConfigurationLoader configurationLoader = new ConfigurationLoader();
            Configuration[] configurations;
            try
            {
                configurations = configurationLoader.Load(Path.GetDirectoryName(this.ProjectPath), this.ConfigurationFile);
            }
            catch (Exception exception)
            {
                this.Log.LogError(string.Empty, string.Empty, string.Empty, this.ConfigurationFile, -1, -1, -1, -1, string.Format("Error during loading configuration: {0}", exception.Message));
                return false;
            }

            // Run generation for each configuration
            bool success = true;
            foreach (Configuration configuration in configurations)
            {
                // Run generation
                ProjbookEngine projbookEngine = new ProjbookEngine(this.ProjectPath, configuration, this.OutputDirectory, this.WkhtmlToPdfLocation);
                GenerationError[] errors = projbookEngine.Generate();

                // Report generation errors
                foreach (GenerationError error in errors)
                {
                    this.Log.LogError(string.Empty, string.Empty, string.Empty, error.SourceFile, -1, -1, -1, -1, error.Message);
                }

                // Stop processing in case of error
                if (errors.Length > 0)
                    success = false;
            }

            // Report processing successful
            return success;
        }
    }
}