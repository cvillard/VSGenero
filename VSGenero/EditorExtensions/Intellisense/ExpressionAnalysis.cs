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

using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VSGenero.Analysis;
using VSGenero.Analysis.Parsing;
using VSGenero.Analysis.Parsing.AST_4GL;

namespace VSGenero.EditorExtensions.Intellisense
{
    /// <summary>
    /// Provides the results of analyzing a simple expression.  Returned from Analysis.AnalyzeExpression.
    /// </summary>
    public class ExpressionAnalysis
    {
        private readonly string _expr;
        private readonly GeneroAst _analysis;
        private readonly ITrackingSpan _span;
        private readonly int _index;
        private readonly GeneroProjectAnalyzer _analyzer;
        private readonly ITextSnapshot _snapshot;
        private readonly IFunctionInformationProvider _functionProvider;
        private readonly IDatabaseInformationProvider _databaseProvider;
        private readonly IProgramFileProvider _programFileProvider;
        private readonly bool _isFunctionCallOrDefinition;
        public static readonly ExpressionAnalysis Empty = new ExpressionAnalysis(null, "", null, 0, null, null, null, null, null, false);

        internal ExpressionAnalysis(GeneroProjectAnalyzer analyzer, string expression, GeneroAst analysis, int index, ITrackingSpan span, ITextSnapshot snapshot,
                                    IFunctionInformationProvider functionProvider, IDatabaseInformationProvider databaseProvider, IProgramFileProvider programFileProvider,
                                    bool isFunctionCallOrDefinition)
        {
            _expr = expression;
            _analysis = analysis;
            _index = index;
            _span = span;
            _analyzer = analyzer;
            _snapshot = snapshot;
            _functionProvider = functionProvider;
            _databaseProvider = databaseProvider;
            _programFileProvider = programFileProvider;
            _isFunctionCallOrDefinition = isFunctionCallOrDefinition;
        }

        /// <summary>
        /// The expression which this is providing information about.
        /// </summary>
        public string Expression
        {
            get
            {
                return _expr;
            }
        }

        /// <summary>
        /// The span of the expression being analyzed.
        /// </summary>
        public ITrackingSpan Span
        {
            get
            {
                return _span;
            }
        }

         ///<summary>
         ///Gets all of the variables (storage locations) associated with the expression.
         ///</summary>
        public IEnumerable<IAnalysisVariable> Variables
        {
            get
            {
                if (_analysis != null)
                {
                    lock (_analyzer)
                    {
                        return _analysis.GetVariablesByIndex(_expr, TranslatedIndex, _functionProvider, _databaseProvider, _programFileProvider, _isFunctionCallOrDefinition);
                    }
                }
                return new IAnalysisVariable[0];
            }
        }

        /// <summary>
        /// The possible values of the expression (types, constants, functions, modules, etc...)
        /// 
        /// This is used by the QuickInfoSource to retrieve info about the expression that's being hovered over
        /// </summary>
        public IAnalysisResult Value
        {
            get
            {
                if (_analysis != null)
                {
                    lock (_analyzer)
                    {
                        if (!_expr.StartsWith("\""))
                        {
                            IGeneroProject dummyProj;
                            IProjectEntry projEntry;
                            bool dummyDef;
                            return _analysis.GetValueByIndex(_expr, TranslatedIndex, _functionProvider, _databaseProvider, _programFileProvider, _isFunctionCallOrDefinition, 
                                                             out dummyDef, out dummyProj, out projEntry, FunctionProviderSearchMode.Search);
                        }
                    }
                }
                return null;
            }
        }

        //public Expression GetEvaluatedExpression()
        //{
        //    return Statement.GetExpression(_analysis.GetAstFromTextByIndex(_expr, TranslatedIndex).Body);
        //}

        /// <summary>
        /// Returns the complete PythonAst for the evaluated expression.  Calling Statement.GetExpression on the Body
        /// of the AST will return the same expression as GetEvaluatedExpression.
        /// 
        /// New in 1.1.
        /// </summary>
        /// <returns></returns>
        //public GeneroAst GetEvaluatedAst()
        //{
        //    return _analysis.GetAstFromTextByIndex(_expr, TranslatedIndex);
        //}

        private int TranslatedIndex
        {
            get
            {
                return GeneroProjectAnalyzer.TranslateIndex(_index, _snapshot, _analysis);
            }
        }
    }
}
