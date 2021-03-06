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
    public abstract class FglStatement : AstNode4gl
    {
    }

    public delegate FglStatement ContextStatementFactory(Genero4glParser parser);

    public class FglStatementFactory
    {
        public bool TryParseNode(Genero4glParser parser, 
                                 out FglStatement node,
                                 IModuleResult containingModule,
                                 List<Func<PrepareStatement, bool>> prepStatementBinders,
                                 Func<ReturnStatement, ParserResult> returnStatementBinder = null,
                                 Action<IAnalysisResult, int, int> limitedScopeVariableAdder = null,
                                 bool returnStatementsOnly = false,
                                 List<TokenKind> validExitKeywords = null,
                                 IEnumerable<ContextStatementFactory> contextStatementFactories = null,
                                 ExpressionParsingOptions expressionOptions = null,
                                 HashSet<TokenKind> endKeywords = null)
        {
            node = null;
            bool result = false;

            if(returnStatementsOnly)
            {
                if(parser.PeekToken(TokenKind.ReturnKeyword))
                {
                    ReturnStatement retStmt;
                    if ((result = ReturnStatement.TryParseNode(parser, out retStmt)))
                    {
                        node = retStmt;
                    }
                }
                return result;
            }

            if(contextStatementFactories != null)
            {
                foreach (var context in contextStatementFactories)
                {
                    node = context(parser);
                    if (node != null)
                        break;
                }
                if (node != null)
                {
                    return true;
                }
            }

            switch (parser.PeekToken().Kind)
            {
                case TokenKind.LetKeyword:
                    {
                        LetStatement letStmt;
                        if ((result = LetStatement.TryParseNode(parser, out letStmt, expressionOptions)))
                        {
                            node = letStmt;
                        }
                        break;
                    }
                case TokenKind.DeclareKeyword:
                    {
                        DeclareStatement declStmt;
                        if ((result = DeclareStatement.TryParseNode(parser, out declStmt, containingModule)))
                        {
                            node = declStmt;
                        }
                        break;
                    }
                case TokenKind.DeferKeyword:
                    {
                        DeferStatementNode deferStmt;
                        if ((result = DeferStatementNode.TryParseNode(parser, out deferStmt)))
                        {
                            node = deferStmt;
                        }
                        break;
                    }
                case TokenKind.PrepareKeyword:
                    {
                        PrepareStatement prepStmt;
                        if ((result = PrepareStatement.TryParseNode(parser, out prepStmt, containingModule)))
                        {
                            node = prepStmt;
                            foreach (var binder in prepStatementBinders)
                                if (binder(prepStmt))
                                    break;
                        }
                        break;
                    }
                case TokenKind.SqlKeyword:
                    {
                        SqlBlockNode sqlStmt;
                        if ((result = SqlBlockNode.TryParseSqlNode(parser, out sqlStmt)))
                        {
                            node = sqlStmt;
                        }
                        break;
                    }
                case TokenKind.ReturnKeyword:
                    {
                        ReturnStatement retStmt;
                        if((result = ReturnStatement.TryParseNode(parser, out retStmt)))
                        {
                            if(returnStatementBinder != null)
                            {
                                var parseResult = returnStatementBinder(retStmt);
                                if(!parseResult.Success && !string.IsNullOrWhiteSpace(parseResult.ErrorMessage))
                                {
                                    parser.ReportSyntaxError(parseResult.ErrorMessage);
                                }
                            }
                            node = retStmt;
                        }
                        break;
                    }
                case TokenKind.CallKeyword:
                    {
                        CallStatement callStmt;
                        if((result = CallStatement.TryParseNode(parser, out callStmt)))
                        {
                            node = callStmt;
                        }
                        break;
                    }
                case TokenKind.IfKeyword:
                    {
                        IfStatement ifStmt;
                        if ((result = IfStatement.TryParseNode(parser, out ifStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                               limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, expressionOptions, endKeywords)))
                        {
                            node = ifStmt;
                        }
                        break;
                    }
                case TokenKind.WhileKeyword:
                    {
                        WhileStatement whileStmt;
                        if ((result = WhileStatement.TryParseNode(parser, out whileStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                  limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, expressionOptions, endKeywords)))
                        {
                            node = whileStmt;
                        }
                        break;
                    }
                case TokenKind.ExitKeyword:
                    {
                        ExitStatement exitStatement;
                        if((result = ExitStatement.TryParseNode(parser, out exitStatement)))
                        {
                            node = exitStatement;
                        }
                        break;
                    }
                case TokenKind.ContinueKeyword:
                    {
                        ContinueStatement contStmt;
                        if((result = ContinueStatement.TryParseNode(parser, out contStmt)))
                        {
                            node = contStmt;
                        }
                        break;
                    }
                case TokenKind.WheneverKeyword:
                    {
                        WheneverStatement wheneverStmt;
                        if((result = WheneverStatement.TryParseNode(parser, out wheneverStmt)))
                        {
                            node = wheneverStmt;
                        }
                        break;
                    }
                case TokenKind.ForKeyword:
                    {
                        ForStatement forStmt;
                        if ((result = ForStatement.TryParserNode(parser, out forStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                 limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, expressionOptions, endKeywords)))
                        {
                            node = forStmt;
                        }
                        break;
                    }
                case TokenKind.CaseKeyword:
                    {
                        CaseStatement caseStmt;
                        if ((result = CaseStatement.TryParseNode(parser, out caseStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                 limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, expressionOptions, endKeywords)))
                        {
                            node = caseStmt;
                        }
                        break;
                    }
                case TokenKind.InitializeKeyword:
                    {
                        InitializeStatement initStmt;
                        if((result = InitializeStatement.TryParseNode(parser, out initStmt)))
                        {
                            node = initStmt;
                        }
                        break;
                    }
                case TokenKind.LocateKeyword:
                    {
                        LocateStatement locateStmt;
                        if((result = LocateStatement.TryParseNode(parser, out locateStmt)))
                        {
                            node = locateStmt;
                        }
                        break;
                    }
                case TokenKind.FreeKeyword:
                    {
                        FreeStatement freeStmt;
                        if((result = FreeStatement.TryParseNode(parser, out freeStmt)))
                        {
                            node = freeStmt;
                        }
                        break;
                    }
                case TokenKind.GotoKeyword:
                    {
                        GotoStatement gotoStmt;
                        if((result = GotoStatement.TryParseNode(parser, out gotoStmt)))
                        {
                            node = gotoStmt;
                        }
                        break;
                    }
                case TokenKind.LabelKeyword:
                    {
                        LabelStatement labelStmt;
                        if((result = LabelStatement.TryParseNode(parser, out labelStmt)))
                        {
                            node = labelStmt;
                        }
                        break;
                    }
                case TokenKind.SleepKeyword:
                    {
                        SleepStatement sleepStmt;
                        if((result = SleepStatement.TryParseNode(parser, out sleepStmt)))
                        {
                            node = sleepStmt;
                        }
                        break;
                    }
                case TokenKind.TryKeyword:
                    {
                        TryCatchStatement tryStmt;
                        if ((result = TryCatchStatement.TryParseNode(parser, out tryStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                     limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = tryStmt;
                        }
                        break;
                    }
                case TokenKind.ValidateKeyword:
                    {
                        ValidateStatement validateStmt;
                        if((result = ValidateStatement.TryParseNode(parser, out validateStmt)))
                        {
                            node = validateStmt;
                        }
                        break;
                    }
                case TokenKind.OptionsKeyword:
                    {
                        OptionsStatement optionsStmt;
                        if((result = OptionsStatement.TryParseNode(parser, out optionsStmt)))
                        {
                            node = optionsStmt;
                        }
                        break;
                    }
                case TokenKind.ExecuteKeyword:
                    {
                        ExecuteStatement exeStmt;
                        if((result = ExecuteStatement.TryParseNode(parser, out exeStmt)))
                        {
                            node = exeStmt;
                        }
                        break;
                    }
                case TokenKind.OpenKeyword:
                    {
                        OpenStatement openStmt;
                        if((result = OpenStatement.TryParseNode(parser, out openStmt)))
                        {
                            node = openStmt;
                        }
                        break;
                    }
                case TokenKind.FetchKeyword:
                    {
                        FetchStatement fetchStmt;
                        if((result = FetchStatement.TryParseNode(parser, out fetchStmt)))
                        {
                            node = fetchStmt;
                        }
                        break;
                    }
                case TokenKind.CloseKeyword:
                    {
                        CloseStatement closeStmt;
                        if((result = CloseStatement.TryParseNode(parser, out closeStmt)))
                        {
                            node = closeStmt;
                        }
                        break;
                    }
                case TokenKind.ClearKeyword:
                    {
                        ClearStatement clearStmt;
                        if((result = ClearStatement.TryParseNode(parser, out clearStmt)))
                        {
                            node = clearStmt;
                        }
                        break;
                    }
                case TokenKind.ForeachKeyword:
                    {
                        ForeachStatement foreachStmt;
                        if ((result = ForeachStatement.TryParseNode(parser, out foreachStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                    limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = foreachStmt;
                        }
                        break;
                    }
                case TokenKind.MessageKeyword:
                    {
                        MessageStatement msgStmt;
                        if((result = MessageStatement.TryParseNode(parser, out msgStmt)))
                        {
                            node = msgStmt;
                        }
                        break;
                    }
                case TokenKind.MenuKeyword:
                    {
                        MenuBlock menuStmt;
                        if ((result = MenuBlock.TryParseNode(parser, out menuStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                             limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = menuStmt;
                            if(limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.InputKeyword:
                    {
                        InputBlock inputStmt;
                        if ((result = InputBlock.TryParseNode(parser, out inputStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                              limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = inputStmt;
                            if (limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.ConstructKeyword:
                    {
                        ConstructBlock constructStmt;
                        if ((result = ConstructBlock.TryParseNode(parser, out constructStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                  limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = constructStmt;
                            if (limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.FlushKeyword:
                    {
                        FlushStatement flushStmt;
                        if((result = FlushStatement.TryParseNode(parser, out flushStmt)))
                        {
                            node = flushStmt;
                        }
                        break;
                    }
                case TokenKind.DisplayKeyword:
                    {
                        DisplayBlock dispStmt;
                        if ((result = DisplayBlock.TryParseNode(parser, out dispStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = dispStmt;
                            if (limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.PromptKeyword:
                    {
                        PromptStatement promptStmt;
                        if((result = PromptStatement.TryParseNode(parser, out promptStmt, containingModule, prepStatementBinders, returnStatementBinder, 
                                                                  limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = promptStmt;
                            if (limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.DialogKeyword:
                    {
                        DialogBlock dialogBlock;
                        if ((result = DialogBlock.TryParseNode(parser, out dialogBlock, containingModule, prepStatementBinders, returnStatementBinder, 
                                                               limitedScopeVariableAdder, validExitKeywords, contextStatementFactories, endKeywords)))
                        {
                            node = dialogBlock;
                            if (limitedScopeVariableAdder != null)
                                limitedScopeVariableAdder(Genero4glAst.DialogVariable, node.StartIndex, node.EndIndex);
                        }
                        break;
                    }
                case TokenKind.AcceptKeyword:
                    {
                        AcceptStatement acceptStmt;
                        if((result = AcceptStatement.TryParseNode(parser, out acceptStmt)))
                        {
                            node = acceptStmt;
                        }
                        break;
                    }
                case TokenKind.LoadKeyword:
                    {
                        LoadStatement loadStmt;
                        if((result = LoadStatement.TryParseNode(parser, out loadStmt)))
                        {
                            node = loadStmt;
                        }
                        break;
                    }
                case TokenKind.CreateKeyword:
                    {
                        CreateStatement createStmt;
                        if((result = CreateStatement.TryParseNode(parser, out createStmt, containingModule)))
                        {
                            node = createStmt;
                        }
                        break;
                    }
                case TokenKind.BreakpointKeyword:
                    {
                        BreakpointStatement brkStmt;
                        if((result = BreakpointStatement.TryParseNode(parser, out brkStmt)))
                        {
                            node = brkStmt;
                        }
                        break;
                    }
                case TokenKind.OutputKeyword:
                    {
                        OutputToReportStatement outRpt;
                        if((result = OutputToReportStatement.TryParseNode(parser, out outRpt)))
                        {
                            node = outRpt;
                        }
                        break;
                    }
                case TokenKind.StartKeyword:
                    {
                        StartReportStatement startRpt;
                        if((result = StartReportStatement.TryParseNode(parser, out startRpt)))
                        {
                            node = startRpt;
                        }
                        break;
                    }
                case TokenKind.FinishKeyword:
                    {
                        FinishReportStatement finRpt;
                        if ((result = FinishReportStatement.TryParseNode(parser, out finRpt)))
                        {
                            node = finRpt;
                        }
                        break;
                    }
                case TokenKind.TerminateKeyword:
                    {
                        TerminateReportStatement termRpt;
                        if ((result = TerminateReportStatement.TryParseNode(parser, out termRpt)))
                        {
                            node = termRpt;
                        }
                        break;
                    }
                default:
                    {
                        if (SqlStatementFactory.IsValidStatementStart(parser.PeekToken().Kind))
                        {
                            bool dummy;
                            result = SqlStatementFactory.TryParseSqlStatement(parser, out node, out dummy);
                        }
                        break;
                    }
            }

            if(result)
            {
                // check for semicolon
                if (parser.PeekToken(TokenKind.Semicolon))
                    parser.NextToken();
            }

            return result;
        }
    }

    public class BreakpointStatement : FglStatement
    {
        public static bool TryParseNode(Genero4glParser parser, out BreakpointStatement node)
        {
            node = null;
            if(parser.PeekToken(TokenKind.BreakpointKeyword))
            {
                node = new BreakpointStatement();
                parser.NextToken();
                node.StartIndex = parser.Token.Span.Start;
                node.EndIndex = parser.Token.Span.End;
                return true;
            }
            return false;
        }
    }
}
