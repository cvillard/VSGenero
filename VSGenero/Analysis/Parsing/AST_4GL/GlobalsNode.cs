﻿/* ****************************************************************************
 * Copyright (c) 2015 Greg Fullman 
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

namespace VSGenero.Analysis.Parsing.AST_4GL
{
    /// <summary>
    /// 
    /// Syntax 1:
    /// GLOBALS
    ///     declaration-statement (variable, constant, or type definition)
    ///     [,...]
    /// END GLOBALS
    /// 
    /// or
    /// 
    /// Syntax 2:
    /// GLOBALS "filename"
    /// 
    /// For more info, see: http://www.4js.com/online_documentation/fjs-fgl-manual-html/index.html#c_fgl_Globals_003.html
    /// </summary>
    public class GlobalsNode : AstNode4gl
    {
        public string GlobalsFilename { get; private set; }

        public static bool TryParseNode(Genero4glParser parser, out GlobalsNode defNode)
        {
            defNode = null;
            bool result = false;

            if(parser.PeekToken(TokenKind.GlobalsKeyword))
            {
                result = true;
                parser.NextToken();
                defNode = new GlobalsNode();
                defNode.StartIndex = parser.Token.Span.Start;

                var tok = parser.PeekToken();
                if(Tokenizer.GetTokenInfo(tok).Category == TokenCategory.StringLiteral)
                {
                    parser.NextToken();
                    defNode.GlobalsFilename = parser.Token.Token.Value.ToString();
                }
                else
                {
                    List<List<TokenKind>> breakSequences = new List<List<TokenKind>>() 
                    { 
                        new List<TokenKind> { TokenKind.EndKeyword, TokenKind.GlobalsKeyword },
                        new List<TokenKind> { TokenKind.ConstantKeyword },
                        new List<TokenKind> { TokenKind.DefineKeyword },
                        new List<TokenKind> { TokenKind.TypeKeyword }
                    };
                    // try to parse one or more declaration statements
                    while(!parser.PeekToken(TokenKind.EndOfFile) &&
                          !(parser.PeekToken(TokenKind.EndKeyword) && parser.PeekToken(TokenKind.GlobalsKeyword, 2)))
                    {
                        DefineNode defineNode;
                        TypeDefNode typeNode;
                        ConstantDefNode constNode;
                        bool matchedBreakSequence = false;
                        switch(parser.PeekToken().Kind)
                        {
                            case TokenKind.TypeKeyword:
                                {
                                    var bsList = new List<List<TokenKind>>(breakSequences);
                                    if (TypeDefNode.TryParseNode(parser, out typeNode, out matchedBreakSequence, breakSequences) && typeNode != null)
                                    {
                                        defNode.Children.Add(typeNode.StartIndex, typeNode);
                                        foreach (var def in typeNode.GetDefinitions())
                                        {
                                            def.Scope = "global type";
                                            if(!defNode.Types.ContainsKey(def.Name))
                                                defNode.Types.Add(def.Name, def);
                                            else
                                                parser.ReportSyntaxError(string.Format("Global type {0} is defined more than once.", def.Name), Severity.Error);
                                        }
                                    }
                                    break;
                                }
                            case TokenKind.ConstantKeyword:
                                {
                                    if (ConstantDefNode.TryParseNode(parser, out constNode, out matchedBreakSequence, breakSequences) && constNode != null)
                                    {
                                        defNode.Children.Add(constNode.StartIndex, constNode);
                                        foreach (var def in constNode.GetDefinitions())
                                        {
                                            def.Scope = "global constant";
                                            if(!defNode.Constants.ContainsKey(def.Name))
                                                defNode.Constants.Add(def.Name, def);
                                            else
                                                parser.ReportSyntaxError(string.Format("Global constant {0} is defined more than once.", def.Name), Severity.Error);
                                        }
                                    }
                                    break;
                                }
                            case TokenKind.DefineKeyword:
                                {
                                    if (DefineNode.TryParseDefine(parser, out defineNode, out matchedBreakSequence, breakSequences) && defineNode != null)
                                    {
                                        defNode.Children.Add(defineNode.StartIndex, defineNode);
                                        foreach (var def in defineNode.GetDefinitions())
                                            foreach (var vardef in def.VariableDefinitions)
                                            {
                                                vardef.Scope = "global variable";
                                                if(!defNode.Variables.ContainsKey(vardef.Name))
                                                    defNode.Variables.Add(vardef.Name, vardef);
                                                else
                                                    parser.ReportSyntaxError(vardef.LocationIndex, (vardef.LocationIndex + vardef.Name.Length),
                                                                             string.Format("Global variable {0} is defined more than once.", vardef.Name), Severity.Error);
                                            }
                                    }
                                    break;
                                }
                        }
                        // if a break sequence was matched, we don't want to advance the token
                        if(!matchedBreakSequence)
                        {
                            // TODO: not sure whether to break or keep going...for right now, let's keep going until we hit the end keyword
                            parser.NextToken();
                        }
                    }

                    if (!parser.PeekToken(TokenKind.EndOfFile))
                    {
                        parser.NextToken();
                        if (parser.PeekToken(TokenKind.GlobalsKeyword))
                        {
                            parser.NextToken();
                            defNode.EndIndex = parser.Token.Span.End;
                            defNode.IsComplete = true;
                        }
                        else
                        {
                            parser.ReportSyntaxError(parser.Token.Span.Start, parser.Token.Span.End, "Invalid end of globals definition.");
                        }
                    }
                    else
                    {
                        parser.ReportSyntaxError("Unexpected end of globals definition");
                    }
                }
            }

            return result;
        }

        private Dictionary<string, IAnalysisResult> _variables;
        public Dictionary<string, IAnalysisResult> Variables
        {
            get
            {
                if (_variables == null)
                    _variables = new Dictionary<string, IAnalysisResult>(StringComparer.OrdinalIgnoreCase);
                return _variables;
            }
        }

        private Dictionary<string, IAnalysisResult> _constants;
        public Dictionary<string, IAnalysisResult> Constants
        {
            get
            {
                if (_constants == null)
                    _constants = new Dictionary<string, IAnalysisResult>(StringComparer.OrdinalIgnoreCase);
                return _constants;
            }
        }

        private Dictionary<string, IAnalysisResult> _types;
        public Dictionary<string, IAnalysisResult> Types
        {
            get
            {
                if (_types == null)
                    _types = new Dictionary<string, IAnalysisResult>(StringComparer.OrdinalIgnoreCase);
                return _types;
            }
        }

        public override int DecoratorEnd
        {
            get
            {
                return StartIndex + 7;
            }
            set
            {
            }
        }

        public override bool CanOutline
        {
            get { return string.IsNullOrWhiteSpace(GlobalsFilename); }
        }
    }
}
