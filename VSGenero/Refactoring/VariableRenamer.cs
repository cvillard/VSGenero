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
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VSGenero.Analysis;
using VSGenero.Analysis.Parsing.AST_4GL;
using VSGenero.EditorExtensions;
using VSGenero.External.Interfaces;

namespace VSGenero.Refactoring
{
    class VariableRenamer
    {
        private readonly ITextView _view;
        private readonly IFunctionInformationProvider _funcProvider;
        private readonly IDatabaseInformationProvider _dbProvider;
        private readonly IProgramFileProvider _progFileProvider;
        private readonly IServiceProvider _serviceProvider;


        public VariableRenamer(ITextView textView, IFunctionInformationProvider funcProvider, IDatabaseInformationProvider dbProvider, IProgramFileProvider progFileProvider, IServiceProvider serviceProvider)
        {
            _view = textView;
            _funcProvider = funcProvider;
            _dbProvider = dbProvider;
            _progFileProvider = progFileProvider;
            _serviceProvider = serviceProvider;
        }

        public void RenameVariable(IRenameVariableInput input, IVsPreviewChangesService previewChanges)
        {
            //if (IsModuleName(input))
            //{
            //    input.CannotRename("Cannot rename a module name");
            //    return;
            //}

            var analysis = _view.GetExpressionAnalysis(_funcProvider, _dbProvider, _progFileProvider);

            string originalName = null;
            string privatePrefix = null;
            if (analysis != null && analysis.Value != null)
            {
                // TODO: really need to add a IRenamable API that allows us to determine whether the item selected can be renamed
                if(analysis.Value is VariableDef || 
                   analysis.Value is FunctionBlockNode)
                {
                    originalName = analysis.Value.Name;
                }
                //expr = Statement.GetExpression(ast.Body);

                //NameExpression ne = expr as NameExpression;
                //MemberExpression me;
                //if (ne != null)
                //{
                //    originalName = ne.Name;
                //}
                //else if ((me = expr as MemberExpression) != null)
                //{
                //    originalName = me.Name;
                //}

                //if (ast.PrivatePrefix != null && originalName != null && originalName.StartsWith("_" + ast.PrivatePrefix))
                //{
                //    originalName = originalName.Substring(ast.PrivatePrefix.Length + 1);
                //    privatePrefix = ast.PrivatePrefix;
                //}

                //if (originalName != null && _view.Selection.IsActive && !_view.Selection.IsEmpty)
                //{
                //    if (_view.Selection.Start.Position < analysis.Span.GetStartPoint(_view.TextBuffer.CurrentSnapshot) ||
                //        _view.Selection.End.Position > analysis.Span.GetEndPoint(_view.TextBuffer.CurrentSnapshot))
                //    {
                //        originalName = null;
                //    }
                //}
            }

            if (originalName == null)
            {
                input.CannotRename("Please select a symbol to be renamed.");
                return;
            }

            bool hasVariables = false;
            foreach (var variable in analysis.Variables)
            {
                if (variable.Type == VariableType.Definition || variable.Type == VariableType.Reference)
                {
                    hasVariables = true;
                    break;
                }
            }

            IEnumerable<IAnalysisVariable> variables = null;
            if (!hasVariables)
            {
                //List<IAnalysisVariable> paramVars = GetKeywordParameters(expr, originalName);

                //if (paramVars.Count == 0)
                //{
                //    input.CannotRename(string.Format("No information is available for the variable '{0}'.", originalName));
                //    return;
                //}

                //variables = paramVars;
            }
            else
            {
                variables = analysis.Variables;

            }

            //PythonLanguageVersion languageVersion = PythonLanguageVersion.None;
            //var analyzer = _view.GetAnalyzer(_serviceProvider);
            //var factory = analyzer != null ? analyzer.InterpreterFactory : null;
            //if (factory != null)
            //{
            //    languageVersion = factory.Configuration.Version.ToLanguageVersion();
            //}

            var info = input.GetRenameInfo(originalName);
            if (info != null)
            {
                var engine = new PreviewChangesEngine(_serviceProvider, input, analysis, info, originalName, privatePrefix, _view.GetAnalyzer(), variables);
                if (info.Preview)
                {
                    previewChanges.PreviewChanges(engine);
                }
                else
                {
                    ErrorHandler.ThrowOnFailure(engine.ApplyChanges());
                }
            }
        }

        //private List<IAnalysisVariable> GetKeywordParameters(Expression expr, string originalName)
        //{
        //    List<IAnalysisVariable> paramVars = new List<IAnalysisVariable>();
        //    if (expr is NameExpression)
        //    {
        //        // let's check if we'r re-naming a keyword argument...
        //        ITrackingSpan span = _view.GetCaretSpan();
        //        var sigs = _view.TextBuffer.CurrentSnapshot.GetSignatures(_serviceProvider, span);

        //        foreach (var sig in sigs.Signatures)
        //        {
        //            IOverloadResult overloadRes = sig as IOverloadResult;
        //            if (overloadRes != null)
        //            {
        //                foreach (var param in overloadRes.Parameters)
        //                {
        //                    if (param.Name == originalName && param.Variables != null)
        //                    {
        //                        paramVars.AddRange(param.Variables);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    return paramVars;
        //}

        //private bool IsModuleName(IRenameVariableInput input)
        //{
        //    // make sure we're in 
        //    var span = _view.GetCaretSpan();
        //    var buffer = span.TextBuffer;
        //    var snapshot = buffer.CurrentSnapshot;
        //    var classifier = buffer.GetPythonClassifier();

        //    bool sawImport = false, sawFrom = false, sawName = false;
        //    var walker = ReverseExpressionParser.ReverseClassificationSpanEnumerator(classifier, span.GetEndPoint(snapshot));
        //    while (walker.MoveNext())
        //    {
        //        var current = walker.Current;
        //        if (current == null)
        //        {
        //            // new-line
        //            break;
        //        }

        //        var text = current.Span.GetText();
        //        if (current.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Identifier))
        //        {
        //            // identifiers are ok
        //            sawName = true;
        //        }
        //        else if (current.ClassificationType == classifier.Provider.DotClassification ||
        //          current.ClassificationType == classifier.Provider.CommaClassification)
        //        {
        //            // dots and commas are ok
        //        }
        //        else if (current.ClassificationType == classifier.Provider.GroupingClassification)
        //        {
        //            if (text != "(" && text != ")")
        //            {
        //                // list/dict groupings are not ok
        //                break;
        //            }
        //        }
        //        else if (current.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Keyword))
        //        {
        //            if (text == "import")
        //            {
        //                sawImport = true;
        //            }
        //            else if (text == "from")
        //            {
        //                sawFrom = true;
        //                break;
        //            }
        //            else if (text == "as")
        //            {
        //                if (sawName)
        //                {
        //                    // import fob as oar
        //                    // from fob import oar as baz
        //                    break;
        //                }
        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }

        //    // we saw from, but not import, so we're renaming a module name (from fob, renaming fob)
        //    // or we saw import, but not a from, so we're renaming a module name
        //    return sawFrom != sawImport;
        //}
    }
}
