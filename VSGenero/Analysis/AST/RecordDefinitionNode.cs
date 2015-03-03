﻿using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSGenero.Analysis.AST
{
    /// <summary>
    /// identifier RECORD [<see cref="AttributeSpecifier"/>]
    ///   member <see cref="TypeReference"/> [<see cref="AttributeSpecifier"/>]
    ///   [,...] 
    /// END RECORD
    /// 
    /// For more info, see: http://www.4js.com/online_documentation/fjs-fgl-manual-html/index.html#c_fgl_records_002.html
    /// </summary>
    public class RecordDefinitionNode : AstNode
    {
        public AttributeSpecifier Attribute { get; private set; }
        public string MimicTableName { get; private set; }
        public string MimicDatabaseName { get; private set; }

        private Dictionary<string, TypeReference> _memberDictionary;
        public Dictionary<string, TypeReference> MemberDictionary
        {
            get
            {
                if(_memberDictionary == null)
                    _memberDictionary = new Dictionary<string,TypeReference>();
                return _memberDictionary;
            }
        }

        public new static bool TryParseNode(Parser parser, out RecordDefinitionNode defNode)
        {
            defNode = null;
            bool result = false;

            if(parser.PeekToken(TokenKind.RecordKeyword))
            {
                result = true;
                defNode = new RecordDefinitionNode();
                defNode.StartIndex = parser.Token.Span.Start;
                parser.NextToken();     // move past the record keyword
                if (parser.PeekToken(TokenKind.LikeKeyword))
                {
                    // get db info
                    parser.NextToken();
                    if(!parser.PeekToken(TokenCategory.Identifier) && parser.PeekToken(TokenKind.Colon, 2))
                    {
                        parser.NextToken(); // advance to the database name
                        defNode.MimicDatabaseName = parser.Token.Token.Value.ToString();
                        parser.NextToken(); // advance to the colon
                    }
                    if(!parser.PeekToken(TokenCategory.Identifier))
                    {
                        parser.ReportSyntaxError("Database table name expected.");
                    }
                    else if(!parser.PeekToken(TokenKind.Dot, 2) && !parser.PeekToken(TokenKind.Multiply))
                    {
                        parser.ReportSyntaxError("A mimicking record must reference a table as follows: \"[tablename].*\".");
                    }
                    else
                    {
                        parser.NextToken(); // advance to the table name
                        defNode.MimicTableName = parser.Token.Token.Value.ToString();
                        parser.NextToken(); // advance to the dot
                        parser.NextToken(); // advance to the star
                    }
                }
                else
                {
                    AttributeSpecifier attribSpec;
                    if(AttributeSpecifier.TryParseNode(parser, out attribSpec))
                    {
                        defNode.Attribute = attribSpec;
                    }

                    while (parser.PeekToken(TokenCategory.Identifier) || parser.PeekToken(TokenCategory.Keyword))
                    {
                        var tok = parser.NextToken();

                        TypeReference tr;
                        if (TypeReference.TryParseNode(parser, out tr, true))
                        {
                            defNode.MemberDictionary.Add(tok.Value.ToString(), tr);
                        }

                        if (parser.MaybeEat(TokenKind.Comma))
                        {
                            continue;
                        }
                        else if (parser.MaybeEat(TokenKind.EndKeyword))
                        {
                            if (!parser.MaybeEat(TokenKind.RecordKeyword))
                            {
                                parser.ReportSyntaxError("Invalid end token in record definition");
                            }
                            break;
                        }
                        else
                        {
                            parser.ReportSyntaxError("Invalid token within record definition");
                            break;
                        }
                    }
                }
            }

            return result;
        }
    }
}
