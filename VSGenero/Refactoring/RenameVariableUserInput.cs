﻿/* ****************************************************************************
 * Copyright (c) 2015 Greg Fullman 
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution.
 * By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Windows;

namespace VSGenero.Refactoring
{
    /// <summary>
    /// Handles input when running the rename refactoring within Visual Studio.
    /// </summary>
    class RenameVariableUserInput : IRenameVariableInput
    {
        private readonly IServiceProvider _serviceProvider;

        private const string RefactorGuidStr = "{5A822660-832B-4AF0-9A86-1048D33A05E7}";
        private static readonly Guid RefactorGuid = new Guid(RefactorGuidStr);
        private const string RefactorKey = "Refactor";
        private const string RenameKey = "Rename";
        private const string PreviewChangesKey = "PreviewChanges";

        public RenameVariableUserInput(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public RenameVariableRequest GetRenameInfo(string originalName)
        {
            var requestView = new RenameVariableRequestView(originalName);
            LoadPreferences(requestView);
            var dialog = new RenameVariableDialog(requestView);
            var res = dialog.ShowModal() ?? false;
            if (!res) return null;
            SavePreferences(requestView);
            return requestView.GetRequest();
        }

        private static void SavePreferences(RenameVariableRequestView requestView)
        {
            SaveBool(PreviewChangesKey, requestView.PreviewChanges);
        }

        private static void LoadPreferences(RenameVariableRequestView requestView)
        {
            requestView.PreviewChanges = LoadBool(PreviewChangesKey) ?? true;
        }

        private static void SaveBool(string name, bool value)
        {
            SaveString(name, value.ToString());
        }

        private static void SaveString(string name, string value)
        {
            using (var pythonKey = VSRegistry.RegistryRoot(__VsLocalRegistryType.RegType_UserSettings, true).CreateSubKey(VSGeneroConstants.BaseRegistryKey))
            {
                if (pythonKey == null) return;
                using (var refactorKey = pythonKey.CreateSubKey(RefactorKey))
                {
                    if (refactorKey == null) return;
                    using (var renameKey = refactorKey.CreateSubKey(RenameKey))
                    {
                        if (renameKey == null) return;
                        renameKey.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
                    }
                }
            }
        }

        private static bool? LoadBool(string name)
        {
            string res = LoadString(name);
            if (res == null)
            {
                return null;
            }

            bool val;
            if (bool.TryParse(res, out val))
            {
                return val;
            }
            return null;
        }

        private static string LoadString(string name)
        {
            using (var pythonKey = VSRegistry.RegistryRoot(__VsLocalRegistryType.RegType_UserSettings, true).CreateSubKey(VSGeneroConstants.BaseRegistryKey))
            {
                if (pythonKey == null) return null;
                using (var refactorKey = pythonKey.CreateSubKey(RefactorKey))
                {
                    if (refactorKey == null) return null;
                    using (var renameKey = refactorKey.CreateSubKey(RenameKey))
                    {
                        return renameKey?.GetValue(name) as string;
                    }
                }
            }
        }

        public void CannotRename(string message)
        {
            MessageBox.Show(message, "Cannot rename", MessageBoxButton.OK);
        }

        public void OutputLog(string message)
        {
            IVsOutputWindowPane pane = GetPane();
            if (pane != null)
            {
                pane.Activate();

                pane.OutputString(message);
                pane.OutputString(Environment.NewLine);
            }
        }

        public void ClearRefactorPane()
        {
            IVsOutputWindowPane pane = GetPane();
            pane?.Clear();
        }

        private IVsOutputWindowPane GetPane()
        {
            IVsOutputWindowPane pane;
            var outWin = (IVsOutputWindow)_serviceProvider.GetService(typeof(IVsOutputWindow));

            var buffer = new char[1024];
            var tmp = RefactorGuid;

            if (ErrorHandler.Succeeded(outWin.GetPane(ref tmp, out pane))) return pane;
            ErrorHandler.ThrowOnFailure(outWin.CreatePane(ref tmp, "Refactor", 1, 1));

            return !ErrorHandler.Succeeded(outWin.GetPane(ref tmp, out pane)) ? null : pane;
        }

        public ITextBuffer GetBufferForDocument(string filename)
        {
            return VSGeneroPackage.GetBufferForDocument(_serviceProvider, filename);
        }


        public IVsLinkedUndoTransactionManager BeginGlobalUndo()
        {
            var linkedUndo = (IVsLinkedUndoTransactionManager)_serviceProvider.GetService(typeof(SVsLinkedUndoTransactionManager));
            ErrorHandler.ThrowOnFailure(linkedUndo.OpenLinkedUndo(
                (uint)LinkedTransactionFlags2.mdtGlobal,
                "Rename Variable"
            ));
            return linkedUndo;
        }


        public void EndGlobalUndo(IVsLinkedUndoTransactionManager linkedUndo)
        {
            ErrorHandler.ThrowOnFailure(linkedUndo.CloseLinkedUndo());
        }
    }
}
