﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace VSGenero.Analysis.Parsing.AST_4GL
{
    public class SystemImportModule : IAnalysisResult
    {
        private readonly string _name;
        private readonly string _desc;
        private readonly Dictionary<string, IFunctionResult> _memberFunctions;
        private readonly GeneroLanguageVersion _minBdlVersion;
        private readonly GeneroLanguageVersion _maxBdlVersion;

        public SystemImportModule(string name, 
                                  string description, 
                                  IEnumerable<BuiltinFunction> functions,
                                  GeneroLanguageVersion minimumBdlVersion = GeneroLanguageVersion.None,
                                  GeneroLanguageVersion maximumBdlVersion = GeneroLanguageVersion.Latest)
        {
            _name = name;
            _desc = description;
            _minBdlVersion = minimumBdlVersion;
            _maxBdlVersion = maximumBdlVersion;
            _memberFunctions = new Dictionary<string, IFunctionResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var func in functions)
                _memberFunctions.Add(func.Name, func);
        }

        public bool CanGetValueFromDebugger
        {
            get
            {
                return false;
            }
        }

        public string Documentation
        {
            get
            {
                return _desc;
            }
        }

        public bool IsPublic
        {
            get
            {
                return true;
            }
        }

        public LocationInfo Location
        {
            get
            {
                return null;
            }
        }

        public int LocationIndex
        {
            get
            {
                return -1;
            }
        }

        public string Name
        {
            get
            {
                return _name;
            }
        }

        private string _scope = "system import module";
        public string Scope
        {
            get
            {
                return _scope;
            }
            set
            {
            }
        }

        public string Typename
        {
            get { return null; }
        }

        public GeneroLanguageVersion MinimumLanguageVersion
        {
            get
            {
                return _minBdlVersion;
            }
        }

        public GeneroLanguageVersion MaximumLanguageVersion
        {
            get
            {
                return _maxBdlVersion;
            }
        }

        public IAnalysisResult GetMember(GetMemberInput input)
        {
            IFunctionResult funcRes = null;
            _memberFunctions.TryGetValue(input.Name, out funcRes);
            return funcRes;
        }

        public IEnumerable<MemberResult> GetMembers(GetMultipleMembersInput input)
        {
            return _memberFunctions.Values.Where(x => input.AST.LanguageVersion >= x.MinimumLanguageVersion && input.AST.LanguageVersion <= x.MaximumLanguageVersion)
                                          .Select(x => new MemberResult(x.Name, x, GeneroMemberType.Function, input.AST));
        }

        public bool HasChildFunctions(Genero4glAst ast)
        {
            return _memberFunctions.Count > 0;
        }
    }
}
