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

namespace VSGenero.Analysis.Parsing.AST_4GL
{
    /// <summary>
    /// Format:
    /// {
    ///     datatype
    ///     |
    ///     LIKE [dbname:]tabname.colname
    /// }
    /// 
    /// For more info, see http://www.4js.com/online_documentation/fjs-fgl-manual-html/index.html#c_fgl_user_types_003.html
    /// or http://www.4js.com/online_documentation/fjs-fgl-manual-html/index.html#c_fgl_variables_DEFINE.html
    /// </summary>
    public class TypeReference : AstNode4gl, IVariableResult
    {
        public AttributeSpecifier Attribute { get; private set; }

        private bool _isPublic;
        public bool IsPublic { get { return _isPublic; } }

        protected string _typeNameString;
        private string _dbTypeNameString;
        private bool _isConstrainedType;
        public string DatabaseName { get; private set; }
        public string TableName { get; private set; }
        public string ColumnName { get; private set; }

        public virtual ITypeResult GetGeneroType()
        {
            var varType = new VariableTypeResult
            {
                IsArray = this.IsArray,
                IsRecord = this.IsRecord,
                Typename = this.Name,
                ArrayDimension = this.ArrayDimension
            };
            if (Children.Count == 1 && Children[Children.Keys[0]] is ArrayTypeReference)
            {
                varType.ArrayType = (Children[Children.Keys[0]] as ArrayTypeReference).GetGeneroType();
            }
            else if (Children.Count == 1 && Children[Children.Keys[0]] is RecordDefinitionNode)
            {
                varType.RecordMemberTypes = (Children[Children.Keys[0]] as RecordDefinitionNode).GetMemberTypes();
            }
            else if (ResolvedType != null && ResolvedType is IVariableResult)
            {
                varType.UnderlyingType = (ResolvedType as IVariableResult).GetGeneroType();
            }
            return varType;
        }

        private IAnalysisResult _resolvedType;
        public virtual IAnalysisResult ResolvedType
        {
            get
            {
                if(_resolvedType == null)
                {
                    if (Children.Count == 1 && Children[Children.Keys[0]] is ArrayTypeReference)
                        _resolvedType = (Children[Children.Keys[0]] as ArrayTypeReference).ResolvedType;
                    else
                    {
                        string errMsg;
                        _resolvedType = GetResolvedType(SyntaxTree as Genero4glAst, out errMsg);
                    }
                }
                return _resolvedType;
            }
            private set { _resolvedType = value; }
        }

        public virtual string SimpleTypeName
        {
            get
            {
                if (Children.Count == 1 && Children[Children.Keys[0]] is ArrayTypeReference)
                    return (Children[Children.Keys[0]] as ArrayTypeReference).SimpleTypeName;
                return _typeNameString;
            }
        }

        public bool CanGetValueFromDebugger
        {
            get { return false; }
        }

        private bool _useResolvedType;
        public override void SetNamespace(string ns)
        {
            // Don't actually set the namespace, but use as a flag to indicate whether we should use the resolved type, if set
            _useResolvedType = !string.IsNullOrWhiteSpace(ns);
        }

        public bool IsRecord
        {
            get
            {
                if(Children.Count == 1 && Children[Children.Keys[0]] is RecordDefinitionNode)
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// This is the default constructor. Don't use this outside of the TypeReference class!
        /// </summary>
        internal TypeReference()
        { }

        internal TypeReference(string typeString)
        {
            _typeNameString = typeString;
        }

        public override string ToString()
        {
            if (Children.Count > 0)
            {
                if (Children.Count == 1)
                {
                    return Children[Children.Keys[0]].ToString();
                }
                else
                {
                    // TODO: this should never happen
                    return null;
                }
            }
            else
            {
                StringBuilder sb = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(TableName) && !string.IsNullOrWhiteSpace(ColumnName))
                {
                    // TODO: we have two options here...not sure what to do
                    // 1) Look up the type from a database provider
                    // 2) Just list the mimicking "table.column"
                    // For right now, we'll go with #2
                    if (string.IsNullOrWhiteSpace(_dbTypeNameString) && VSGeneroPackage.Instance != null && VSGeneroPackage.Instance.GlobalDatabaseProvider != null)
                    {
                        _dbTypeNameString = VSGeneroPackage.Instance.GlobalDatabaseProvider.GetColumnType(TableName, ColumnName);
                    }
                    sb.AppendFormat("like {0}.{1}", TableName, ColumnName);
                    if (!string.IsNullOrWhiteSpace(_dbTypeNameString))
                        sb.AppendFormat(" ({0})", _dbTypeNameString);
                }
                else
                {
                    if (_useResolvedType && ResolvedType != null)
                        return ResolvedType.Name;
                    else
                        return _typeNameString;
                }

                return sb.ToString();
            }
        }

        public static bool TryParseNode(IParser parser, out TypeReference defNode, bool mimickingRecord = false, bool isPublic = false)
        {
            defNode = null;
            bool result = false;

            ArrayTypeReference arrayType;
            RecordDefinitionNode recordDef;
            FunctionTypeReference functionType;
            DictionaryDefinitionNode dictType;
            if (ArrayTypeReference.TryParseNode(parser, out arrayType) && arrayType != null)
            {
                result = true;
                defNode = new TypeReference();
                defNode.StartIndex = arrayType.StartIndex;
                defNode.EndIndex = arrayType.EndIndex;
                defNode._isPublic = isPublic;
                defNode.Children.Add(arrayType.StartIndex, arrayType);
                defNode.IsComplete = true;
            }
            else if(parser.LanguageVersion >= GeneroLanguageVersion.V310 &&
                    FunctionTypeReference.TryParseNode(parser, out functionType, isPublic) && functionType != null)
            {
                result = true;
                //defNode = new TypeReference();
                //defNode.StartIndex = functionType.StartIndex;
                //defNode.EndIndex = functionType.EndIndex;
                defNode = functionType;
                defNode._isPublic = isPublic;
                //defNode.Children.Add(functionType.StartIndex, functionType);
                defNode.IsComplete = true;
            }
            else if(parser.LanguageVersion >= GeneroLanguageVersion.V310 &&
                    DictionaryDefinitionNode.TryParseNode(parser, out dictType) && dictType != null)
            {
                result = true;
                defNode = new TypeReference();
                defNode.StartIndex = dictType.StartIndex;
                defNode.EndIndex = dictType.EndIndex;
                defNode._isPublic = isPublic;
                defNode.Children.Add(dictType.StartIndex, dictType);
                defNode.IsComplete = true;
            }    
            else if (RecordDefinitionNode.TryParseNode(parser, out recordDef, isPublic) && recordDef != null)
            {
                result = true;
                defNode = new TypeReference();
                defNode.StartIndex = recordDef.StartIndex;
                defNode.EndIndex = recordDef.EndIndex;
                defNode._isPublic = isPublic;
                defNode.Children.Add(recordDef.StartIndex, recordDef);
                defNode.IsComplete = true;
            }
            else if (parser.PeekToken(TokenKind.LikeKeyword))
            {
                result = true;
                parser.NextToken();
                defNode = new TypeReference();
                defNode.StartIndex = parser.Token.Span.Start;
                defNode._isPublic = isPublic;

                // get db info
                if (!parser.PeekToken(TokenCategory.Identifier) && parser.PeekToken(TokenKind.Colon, 2))
                {
                    parser.NextToken(); // advance to the database name
                    defNode.DatabaseName = parser.Token.Token.Value.ToString();
                    parser.NextToken(); // advance to the colon
                }
                if (!parser.PeekToken(TokenCategory.Identifier))
                {
                    parser.ReportSyntaxError("Database table name expected.");
                }
                else if (!parser.PeekToken(TokenKind.Dot, 2) && !parser.PeekToken(TokenCategory.Identifier))
                {
                    parser.ReportSyntaxError("A mimicking type must reference a table as follows: \"[tablename].[recordname]\".");
                }
                else
                {
                    parser.NextToken(); // advance to the table name
                    defNode.TableName = parser.Token.Token.Value.ToString();
                    parser.NextToken(); // advance to the dot
                    if (parser.Token.Token.Kind == TokenKind.Dot)
                    {
                        if (parser.PeekToken(TokenKind.Multiply) ||
                            parser.PeekToken(TokenCategory.Identifier) ||
                            parser.PeekToken(TokenCategory.Keyword))
                        {
                            parser.NextToken(); // advance to the column name
                            defNode.ColumnName = parser.Token.Token.Value.ToString();
                            if (!mimickingRecord && defNode.ColumnName == "*")
                            {
                                parser.ReportSyntaxError("A variable cannot mimic an entire table without being a record. The variable must be defined as a mimicking record.");
                            }
                            defNode.IsComplete = true;
                            defNode.EndIndex = parser.Token.Span.End;
                        }
                        else
                        {
                            if (mimickingRecord)
                                parser.ReportSyntaxError("A mimicking variable must use the format \"like table.*\"");
                            else
                                parser.ReportSyntaxError("A mimicking variable must use the format \"like table.column\"");
                        }
                    }
                    else
                    {
                        if (mimickingRecord)
                            parser.ReportSyntaxError("A mimicking variable must use the format \"like table.*\"");
                        else
                            parser.ReportSyntaxError("A mimicking variable must use the format \"like table.column\"");
                    }
                }
            }
            else
            {
                var tok = parser.PeekToken();
                var cat = Tokenizer.GetTokenInfo(tok).Category;
                if (cat == TokenCategory.Keyword || cat == TokenCategory.Identifier)
                {
                    StringBuilder sb = new StringBuilder();
                    result = true;
                    parser.NextToken();
                    defNode = new TypeReference();
                    defNode.StartIndex = parser.Token.Span.Start;
                    sb.Append(parser.Token.Token.Value.ToString());

                    // determine if there are any constraints on the type keyword
                    string typeString;
                    if (TypeConstraints.VerifyValidConstraint(parser, out typeString))
                    {
                        defNode._typeNameString = typeString;
                        defNode._isConstrainedType = true;
                    }
                    else
                    {
                        // see if we're referencing an extension type (dotted name)
                        while (parser.PeekToken(TokenKind.Dot))
                        {
                            sb.Append(parser.NextToken().Value.ToString());
                            if (parser.PeekToken(TokenCategory.Keyword) || parser.PeekToken(TokenCategory.Identifier))
                            {
                                sb.Append(parser.NextToken().Value.ToString());
                            }
                            else
                            {
                                parser.ReportSyntaxError("Unexpected token in type reference.");
                            }
                        }
                        defNode._typeNameString = sb.ToString();
                    }

                    AttributeSpecifier attribSpec;
                    if (AttributeSpecifier.TryParseNode(parser, out attribSpec))
                    {
                        defNode.Attribute = attribSpec;
                    }

                    defNode.EndIndex = parser.Token.Span.End;
                    defNode.IsComplete = true;
                }
                else if (cat == TokenCategory.EndOfStream)
                {
                    parser.ReportSyntaxError("Unexpected end of type definition");
                    result = false;
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

        public virtual string Name
        {
            get { return ToString(); }
        }

        public int LocationIndex
        {
            get { return StartIndex; }
        }

        protected LocationInfo _location;
        public LocationInfo Location { get { return _location; } }

        public IAnalysisResult GetMember(GetMemberInput input)
        {
            string name = input.Name;
            // need to handle cases where the name is a function call. I think the only time this would happen is if name ends with ')'
            if (name.EndsWith(")"))
            {
                // try to get the function from the class
                int firstParen = name.IndexOf('(');
                if (firstParen > 0)
                {
                    name = name.Substring(0, firstParen);
                }
            }
            //else
            //{
            if (!string.IsNullOrWhiteSpace(TableName))
            {
                // TODO: get the specified column from the database provider
                return null;
            }
            else
            {
                MemberType memType = Analysis.MemberType.All;
                // TODO: there's probably a better way to do this
                var member = GetAnalysisMembers(memType, input).Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if(member is AstNode4gl)
                {
                    input.ProjectEntry = (member as AstNode4gl).SyntaxTree.ProjectEntry;
                    input.DefiningProject = (member as AstNode4gl).SyntaxTree.ProjectEntry.ParentProject;
                }
                else if(member is VariableDef)
                {
                    input.ProjectEntry = (member as VariableDef).SyntaxTree.ProjectEntry;
                    input.DefiningProject = (member as VariableDef).SyntaxTree.ProjectEntry.ParentProject;
                }
                return member;
            }
            //}
        }

        internal IEnumerable<IAnalysisResult> GetAnalysisMembers(MemberType memberType, GetMemberInput input)
        {
            bool dummyDef;
            List<IAnalysisResult> members = new List<IAnalysisResult>();
            if (Children.Count == 1)
            {
                // we have an array type or a record type definition
                var node = Children[Children.Keys[0]];
                if (node is ArrayTypeReference)
                {
                    return (node as ArrayTypeReference).GetAnalysisResults(memberType, input);
                }
                else if (node is RecordDefinitionNode)
                {
                    return (node as RecordDefinitionNode).GetAnalysisResults(input.AST);
                }
                else if(node is DictionaryDefinitionNode)
                {
                    return (node as DictionaryDefinitionNode).GetAnalysisResults(memberType, input);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(TableName))
                {
                    // TODO: return the table's columns
                    int i = 0;
                }
                else if (_typeNameString != null && _typeNameString.Equals("string", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.StringFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion);
                }
                else if(_typeNameString != null && _typeNameString.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.TextFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion);
                }
                else if(_typeNameString != null && _typeNameString.Equals("byte", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.ByteFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion);
                }
                else
                {
                    if (ResolvedType != null && 
                        ResolvedType is TypeDefinitionNode &&
                        (ResolvedType as TypeDefinitionNode).TypeRef != null)
                    {
                        return (ResolvedType as TypeDefinitionNode).TypeRef.GetAnalysisMembers(memberType, input);
                    }

                    var gmi = new GetMultipleMembersInput
                    {
                        AST = input.AST,
                        MemberType = memberType,
                        GetArrayTypeMembers = input.IsFunction
                    };

                    // try to determine if the _typeNameString is a user defined type, in which case we need to call its GetMembers function
                    IAnalysisResult udt = (input.AST as Genero4glAst).TryGetUserDefinedType(_typeNameString, LocationIndex);
                    if (udt != null)
                    {
                        return udt.GetMembers(gmi).Select(x => x.Var).Where(y => y != null);
                    }

                    IGeneroProject definingProject;
                    IProjectEntry projectEntry;
                    if (input.AST.ProjectEntry != null)
                    {
                        foreach (var includedFile in input.AST.ProjectEntry.GetIncludedFiles())
                        {
                            if (includedFile.Analysis != null)
                            {
                                var res = includedFile.Analysis.GetValueByIndex(_typeNameString, 1, null, null, null, false, out dummyDef, out definingProject, out projectEntry);
                                if (res != null)
                                {
                                    input.DefiningProject = definingProject;
                                    input.ProjectEntry = projectEntry;
                                    return res.GetMembers(gmi).Select(x => x.Var).Where(y => y != null);
                                }
                            }
                        }

                        // try to get the _typeNameString from types available in imported modules
                        if (input.AST.ProjectEntry.ParentProject.ReferencedProjects.Count > 0)
                        {
                            foreach (var refProj in input.AST.ProjectEntry.ParentProject.ReferencedProjects.Values)
                            {
                                if (refProj is GeneroProject)
                                {
                                    IProjectEntry dummyProj;
                                    udt = (refProj as GeneroProject).GetMemberOfType(_typeNameString, input.AST, false, true, false, false, out dummyProj);
                                    if (udt != null)
                                    {
                                        input.DefiningProject = refProj;
                                        input.ProjectEntry = dummyProj;
                                        return udt.GetMembers(gmi).Select(x => x.Var).Where(y => y != null);
                                    }
                                }
                            }
                        }
                    }

                    
                    // check for package class
                    udt = input.AST.GetValueByIndex(_typeNameString, LocationIndex, input.AST._functionProvider, input.AST._databaseProvider, input.AST._programFileProvider, false, out dummyDef, out definingProject, out projectEntry);
                    if (udt != null)
                    {
                        input.DefiningProject = definingProject;
                        input.ProjectEntry = projectEntry;
                        return udt.GetMembers(gmi).Select(x => x.Var).Where(y => y != null);
                    }
                }
            }
            return members;
        }

        public bool IsArray
        {
            get
            {
                if (Children.Count == 1)
                {
                    // we have an array type or a record type definition
                    var node = Children[Children.Keys[0]];
                    if (node is ArrayTypeReference)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public int ArrayDimension
        {
            get
            {
                if (Children.Count == 1)
                {
                    // we have an array type or a record type definition
                    var node = Children[Children.Keys[0]];
                    if (node is ArrayTypeReference)
                    {
                        return (int)(node as ArrayTypeReference).DynamicArrayDimension;
                    }
                }
                return 0;
            }
        }

        internal IEnumerable<MemberResult> GetArrayMembers(GetMultipleMembersInput input)
        {
            List<MemberResult> members = new List<MemberResult>();
            if (Children.Count == 1)
            {
                // we have an array type or a record type definition
                var node = Children[Children.Keys[0]];
                if (node is ArrayTypeReference)
                {
                    return (node as ArrayTypeReference).GetMembersInternal(input);
                }
            }
            return members;
        }

        public IEnumerable<MemberResult> GetMembers(GetMultipleMembersInput input)
        {
            bool dummyDef;
            List<MemberResult> members = new List<MemberResult>();

            if (Children.Count == 1)
            {
                // we have an array type or a record type definition
                var node = Children[Children.Keys[0]];
                if (node is ArrayTypeReference)
                {
                    return (node as ArrayTypeReference).GetMembersInternal(input);
                }
                else if (node is RecordDefinitionNode)
                {
                    return (node as RecordDefinitionNode).GetMembers(input);
                }
                else if (node is DictionaryDefinitionNode)
                {
                    return (node as DictionaryDefinitionNode).GetMembersInternal(input);
                }
            }
            else if(!string.IsNullOrEmpty(_typeNameString))
            {
                if (!string.IsNullOrWhiteSpace(TableName))
                {
                    // TODO: return the table's columns
                }
                else if (_typeNameString.Equals("string", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.StringFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion)
                                                              .Select(x => new MemberResult(x.Name, x, GeneroMemberType.Method, input.AST));
                }
                else if (_typeNameString.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.TextFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion)
                                                            .Select(x => new MemberResult(x.Name, x, GeneroMemberType.Method, input.AST));
                }
                else if (_typeNameString.Equals("byte", StringComparison.OrdinalIgnoreCase))
                {
                    return Genero4glAst.ByteFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion)
                                                            .Select(x => new MemberResult(x.Name, x, GeneroMemberType.Method, input.AST));
                }
                else
                {
                    if(ResolvedType != null)
                    {
                        return ResolvedType.GetMembers(input);
                    }

                    // try to determine if the _typeNameString is a user defined type (or package class), in which case we need to call its GetMembers function
                    IAnalysisResult udt = (input.AST as Genero4glAst).TryGetUserDefinedType(_typeNameString, LocationIndex);
                    if (udt != null)
                    {
                        return udt.GetMembers(input);
                    }

                    foreach (var includedFile in input.AST.ProjectEntry.GetIncludedFiles())
                    {
                        if (includedFile.Analysis != null)
                        {
                            IGeneroProject dummyProj;
                            IProjectEntry dummyProjEntry;
                            var res = includedFile.Analysis.GetValueByIndex(_typeNameString, 1, null, null, null, false, out dummyDef, out dummyProj, out dummyProjEntry);
                            if (res != null)
                            {
                                return res.GetMembers(input);
                            }
                        }
                    }

                    if (input.AST.ProjectEntry.ParentProject.ReferencedProjects.Count > 0)
                    {
                        foreach (var refProj in input.AST.ProjectEntry.ParentProject.ReferencedProjects.Values)
                        {
                            if (refProj is GeneroProject)
                            {
                                IProjectEntry dummyProj;
                                udt = (refProj as GeneroProject).GetMemberOfType(_typeNameString, input.AST, false, true, false, false, out dummyProj);
                                if (udt != null)
                                {
                                    return udt.GetMembers(input);
                                }
                            }
                        }
                    }

                    // check for package class
                    IGeneroProject dummyProject;
                    IProjectEntry projEntry;
                    udt = input.AST.GetValueByIndex(_typeNameString, LocationIndex, input.AST._functionProvider, input.AST._databaseProvider, input.AST._programFileProvider, false, out dummyDef, out dummyProject, out projEntry);
                    if (udt != null)
                    {
                        return udt.GetMembers(input);
                    }
                }
            }
            return members;
        }


        public bool HasChildFunctions(Genero4glAst ast)
        {
            if (Children.Count == 1)
            {
                var node = Children[Children.Keys[0]];
                if (node is ArrayTypeReference)
                {
                    return true;
                }
                else if (node is RecordDefinitionNode)
                {
                    return (node as RecordDefinitionNode).HasChildFunctions(ast);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_typeNameString))
                {
                    if (_typeNameString.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    else
                    {
                        // try to determine if the _typeNameString is a user defined type
                        IAnalysisResult udt = ast.TryGetUserDefinedType(_typeNameString, LocationIndex);
                        if (udt != null)
                        {
                            return udt.HasChildFunctions(ast);
                        }

                        // check for package class
                        IGeneroProject dummyProj;
                        IProjectEntry projEntry;
                        bool dummyDef;
                        udt = ast.GetValueByIndex(_typeNameString, LocationIndex, ast._functionProvider, ast._databaseProvider, ast._programFileProvider, false, out dummyDef, out dummyProj, out projEntry);
                        if (udt != null)
                        {
                            return udt.HasChildFunctions(ast);
                        }
                    }
                }
            }
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

        public override void CheckForErrors(GeneroAst ast, Action<string, int, int> errorFunc, Dictionary<string, List<int>> deferredFunctionSearches, FunctionProviderSearchMode searchInFunctionProvider = FunctionProviderSearchMode.NoSearch, bool isFunctionCallOrDefinition = false)
        {
            if (Children.Count > 0)
            {
                base.CheckForErrors(ast, errorFunc, deferredFunctionSearches, searchInFunctionProvider, isFunctionCallOrDefinition);
            }
            else if (!string.IsNullOrWhiteSpace(DatabaseName) ||
                     !string.IsNullOrWhiteSpace(TableName) ||
                     !string.IsNullOrWhiteSpace(ColumnName))
            {
                // TODO: do deferred checking of these
            }
            else if(_typeNameString != null && !_isConstrainedType)
            {
                // see if the _typeNameString is a base type
                var tok = Tokens.GetToken(_typeNameString);
                if(tok == null || !Genero4glAst.BuiltinTypes.Contains(tok.Kind))
                {
                    string resolveErrMsg;
                    var resolvedType = GetResolvedType(ast as Genero4glAst, out resolveErrMsg);
                    if(resolvedType == null && resolveErrMsg != null)
                        errorFunc(resolveErrMsg, StartIndex, EndIndex);
                    else
                        ResolvedType = resolvedType;
                }
            }
        }

        private IAnalysisResult GetResolvedType(Genero4glAst ast, out string errMsg)
        {
            errMsg = null;
            IAnalysisResult result = null;
            // if it's not a base type, look up the type
            IGeneroProject dummyProj;
            IProjectEntry dummyProjEntry;
            bool isDeferred;
            var res = Genero4glAst.GetValueByIndex(_typeNameString, StartIndex, ast as Genero4glAst, out dummyProj, out dummyProjEntry, out isDeferred, FunctionProviderSearchMode.NoSearch, false, true, false, false);
            if (res == null)
            {
                errMsg = string.Format("Type {0} not found.", _typeNameString);
            }
            else
            {
                if (res is GeneroPackageClass ||
                   res is TypeDefinitionNode)
                {
                    result = res;
                }
                else
                {
                    errMsg = string.Format("Invalid type {0} found.", _typeNameString);
                }
            }
            return result;
        }

        
    }
}
