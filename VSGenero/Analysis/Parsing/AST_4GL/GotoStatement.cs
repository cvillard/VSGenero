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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSGenero.Analysis.Parsing.AST_4GL
{
    public class GotoStatement : FglStatement
    {
        public FglNameExpression LabelId { get; private set; }

        public static bool TryParseNode(Genero4glParser parser, out GotoStatement node)
        {
            node = null;
            bool result = false;

            if(parser.PeekToken(TokenKind.GotoKeyword))
            {
                result = true;
                node = new GotoStatement();
                parser.NextToken();
                node.StartIndex = parser.Token.Span.Start;
                
                // colon is optional
                if (parser.PeekToken(TokenKind.Colon))
                    parser.NextToken();

                FglNameExpression expr;
                if (!FglNameExpression.TryParseNode(parser, out expr))
                    parser.ReportSyntaxError("Invalid name found in goto statement.");
                else
                    node.LabelId = expr;

                node.EndIndex = parser.Token.Span.End;
            }

            return result;
        }
    }
}
