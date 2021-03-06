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
    /// OPTIONS
    /// { SHORT CIRCUIT
    /// } [,...]
    /// 
    /// For more info, see: http://www.4js.com/online_documentation/fjs-fgl-manual-html/index.html#c_fgl_programs_OPTIONS_compiler.html
    /// </summary>
    public class CompilerOptionsNode : AstNode4gl
    {
        public static bool TryParseNode(Genero4glParser parser, out CompilerOptionsNode defNode)
        {
            defNode = null;
            if(parser.PeekToken(TokenKind.OptionsKeyword))
            {
                parser.NextToken();
                defNode = new CompilerOptionsNode();
                defNode.StartIndex = parser.Token.Span.Start;

                // read options
                if (parser.PeekToken(TokenKind.ShortKeyword))
                {
                    parser.NextToken();
                    if (parser.PeekToken(TokenKind.CircuitKeyword))
                        parser.NextToken();
                    else
                        parser.ReportSyntaxError("The only compiler options are: short circuit");
                }
                else
                {
                    parser.ReportSyntaxError("The only compiler options are: short circuit");
                }

                defNode.EndIndex = parser.Token.Span.End;
                defNode.IsComplete = true;

                return true;
            }
            return false;
        }
    }
}
