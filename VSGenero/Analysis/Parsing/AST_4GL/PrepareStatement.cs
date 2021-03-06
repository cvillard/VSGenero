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

using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using VSGenero.SqlSupport;

namespace VSGenero.Analysis.Parsing.AST_4GL
{
    public class PrepareStatement : FglStatement, IAnalysisResult
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        public string Identifier { get; private set; }

        public bool CanGetValueFromDebugger
        {
            get { return false; }
        }

        public bool IsPublic { get { return false; } }

        public static bool TryParseNode(Genero4glParser parser, out PrepareStatement defNode, IModuleResult containingModule)
        {
            defNode = null;
            bool result = false;

            if (parser.PeekToken(TokenKind.PrepareKeyword))
            {
                result = true;
                defNode = new PrepareStatement();
                parser.NextToken();
                defNode.StartIndex = parser.Token.Span.Start;

                if (parser.PeekToken(TokenCategory.Identifier) || parser.PeekToken(TokenCategory.Keyword))
                {
                    parser.NextToken();
                    defNode.Identifier = parser.Token.Token.Value.ToString();

                    if (parser.PeekToken(TokenKind.FromKeyword))
                    {
                        parser.NextToken();

                        ExpressionNode exprNode;
                        if (FglExpressionNode.TryGetExpressionNode(parser, out exprNode) && exprNode != null)
                        {
                            defNode.Children.Add(exprNode.StartIndex, exprNode);
                            defNode.EndIndex = exprNode.EndIndex;
                            defNode.IsComplete = true;

                            containingModule.BindCursorResult(defNode, parser);
                        }
                        else
                        {
                            parser.ReportSyntaxError("SQL prepare statement must specify an expression to prepare.");
                        }
                    }
                    else
                    {
                        parser.ReportSyntaxError("SQL prepare statement is missing keyword \"from\".");
                    }
                }
                else
                {
                    parser.ReportSyntaxError("SQL prepare statement must specify an identifier to prepare.");
                }
            }

            return result;
        }

        public string Scope
        {
            get
            {
                return null;
            }
            set
            {
            }
        }

        public string Name
        {
            get { return Identifier; }
        }

        private string _sqlStatement;
        public void SetSqlStatement(string sqlStmt)
        {
            _sqlStatement = sqlStmt;
        }

        public override string Documentation
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("prepared cursor {0}:", Name);
                sb.Append("\n\n");
                if (!string.IsNullOrWhiteSpace(_sqlStatement))
                {
                    string formatted = null;
                    try
                    {
                        SqlStatementExtractor.FormatSqlStatement(_sqlStatement);
                    }
                    catch (Exception ex)
                    {
                        _logger.Info(ex, "Unable to format SQL");
                    }
                    if (formatted != null)
                        sb.Append(formatted);
                    else
                        sb.Append(_sqlStatement);
                }
                else
                {
                    sb.Append("Could not extract SQL text.\nThis is likely because the cursor is dynamically generated.");
                }
                return sb.ToString();
            }
        }

        public int LocationIndex
        {
            get { return StartIndex; }
        }

        public LocationInfo Location { get { return null; } }

        public IAnalysisResult GetMember(GetMemberInput input)
        {
            return null;
        }

        public IEnumerable<MemberResult> GetMembers(GetMultipleMembersInput input)
        {
            return null;
        }


        public bool HasChildFunctions(Genero4glAst ast)
        {
            return false;
        }

        public string Typename
        {
            get { return null; }
        }

        public GeneroLanguageVersion MinimumLanguageVersion
        {
            get
            {
                return GeneroLanguageVersion.None;
            }
        }

        public GeneroLanguageVersion MaximumLanguageVersion
        {
            get
            {
                return GeneroLanguageVersion.Latest;
            }
        }
    }
}
