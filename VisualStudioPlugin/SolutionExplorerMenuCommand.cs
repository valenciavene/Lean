﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE80;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.VisualStudioPlugin
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class SolutionExplorerMenuCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int SendForBacktestingCommandId = 0x0100;
        public const int SaveToQuantConnectCommandId = 0x0110;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("00ce2ccb-74c7-42f4-bf63-52c573fc1532");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package _package;

        private DTE2 _dte2;

        private ProjectFinder _projectFinder;

        private LogInCommand _logInCommand;

        /// <summary>
        /// Initializes a new instance of the <see cref="SolutionExplorerMenuCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private SolutionExplorerMenuCommand(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }


            _package = package;
            _dte2 = ServiceProvider.GetService(typeof(SDTE)) as DTE2;
            _projectFinder = CreateProjectFinder();
            _logInCommand = CreateLogInCommand();

            var commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                RegisterSendForBacktesting(commandService);
                RegisterSaveToQuantConnect(commandService);
            }
        }

        private ProjectFinder CreateProjectFinder()
        {
            return new ProjectFinder(PathUtils.GetSolutionFolder(_dte2));
        }

        private LogInCommand CreateLogInCommand()
        {
            return new LogInCommand(PathUtils.GetSolutionFolder(_dte2));
        }

        private void RegisterSendForBacktesting(OleMenuCommandService commandService)
        {
            var menuCommandID = new CommandID(CommandSet, SendForBacktestingCommandId);
            var oleMenuItem = new OleMenuCommand(new EventHandler(SendForBacktestingCallback), menuCommandID);
            commandService.AddCommand(oleMenuItem);
        }

        private void RegisterSaveToQuantConnect(OleMenuCommandService commandService)
        {
            var menuCommandID = new CommandID(CommandSet, SaveToQuantConnectCommandId);
            var oleMenuItem = new OleMenuCommand(new EventHandler(SaveToQuantConnectCallback), menuCommandID);
            commandService.AddCommand(oleMenuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static SolutionExplorerMenuCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return _package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new SolutionExplorerMenuCommand(package);
        }

        private void SendForBacktestingCallback(object sender, EventArgs e)
        {
            ExecuteOnProject(sender, (selectedProjectId, selectedProjectName, files) =>
            {
                var message = string.Format(CultureInfo.CurrentCulture, "Send for backtesting to project {0}, files: {1}", selectedProjectId + " : " + selectedProjectName, string.Join(" ", files));
                var title = "SendToBacktesting";

                // Show a message box to prove we were here
                VsShellUtilities.ShowMessageBox(
                    this.ServiceProvider,
                    message,
                    title,
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            });
        }

        private void SaveToQuantConnectCallback(object sender, EventArgs e)
        {
            ExecuteOnProject(sender, (selectedProjectId, selectedProjectName, files) =>
            {
                var api = AuthorizationManager.GetInstance().GetApi();
                //Api.ProjectResponse r = api.CreateProject("My first C# project", Language.CSharp);

                var message = string.Format(CultureInfo.CurrentCulture, "Save to project {0}, files {1}", selectedProjectId + " : " + selectedProjectName , string.Join(" ", files));
                var title = "SaveToQuantConnect";

                // Show a message box to prove we were here
                VsShellUtilities.ShowMessageBox(
                    this.ServiceProvider,
                    message,
                    title,
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            });
        }

        private void ExecuteOnProject(object sender, Action<int, string, List<string>> onProject)
        {
            if (_logInCommand.DoLogIn(this.ServiceProvider))
            {
                var api = AuthorizationManager.GetInstance().GetApi();
                var projects = api.ListProjects().Projects;
                var projectNames = projects.Select(p => Tuple.Create(p.ProjectId, p.Name)).ToList();

                var files = GetSelectedFiles(sender);
                var fileNames = files.Select(tuple => tuple.Item1).ToList();
                var suggestedProjectName = _projectFinder.ProjectNameForFiles(fileNames);
                var projectNameDialog = new ProjectNameDialog(projectNames, suggestedProjectName);
                VsUtils.DisplayDialogWindow(projectNameDialog);

                if (projectNameDialog.ProjectNameProvided())
                {
                    var selectedProjectName = projectNameDialog.GetSelectedProjectName();
                    var selectedProjectId = projectNameDialog.GetSelectedProjectId();

                    _projectFinder.AssociateProjectWith(selectedProjectName, fileNames);

                    onProject.Invoke(selectedProjectId, selectedProjectName, fileNames);
                }
            }
        }

        private List<Tuple<string, string>> GetSelectedFiles(object sender)
        {
            var myCommand = sender as OleMenuCommand;

            var selectedFiles = new List<Tuple<string, string>>();
            var selectedItems = (object[])_dte2.ToolWindows.SolutionExplorer.SelectedItems;
            foreach (EnvDTE.UIHierarchyItem selectedUIHierarchyItem in selectedItems)
            {
                if (selectedUIHierarchyItem.Object is EnvDTE.ProjectItem)
                {
                    var item = selectedUIHierarchyItem.Object as EnvDTE.ProjectItem;
                    var filePath = item.Properties.Item("FullPath").Value.ToString();
                    var fileAndItsPath = new Tuple<string, string>(item.Name, filePath);
                    selectedFiles.Add(fileAndItsPath);
                }
            }
            return selectedFiles;
        }
    }
}
