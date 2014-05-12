﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VSGenero.EditorExtensions
{
    internal class GeneroProgramContentsManager
    {
        private Dictionary<string, GeneroModuleContents> _programs;
        public Dictionary<string, GeneroModuleContents> Programs
        {
            get
            {
                if (_programs == null)
                    _programs = new Dictionary<string, GeneroModuleContents>();
                return _programs;
            }
        }

        public void AddProgramContents(string program, GeneroModuleContents newContents)
        {
            GeneroModuleContents gmc;
            if (!Programs.TryGetValue(program, out gmc))
            {
                gmc = new GeneroModuleContents();
                gmc.ContentFilename = "global";
            }

            // update the global variables dictionary
            foreach (var globalVarKvp in newContents.GlobalVariables)
            {
                gmc.GlobalVariables.AddOrUpdate(globalVarKvp.Key, globalVarKvp.Value, (x, y) => globalVarKvp.Value);
            }

            // update the global constants dictionary
            foreach (var globalVarKvp in newContents.GlobalConstants)
            {
                gmc.GlobalConstants.AddOrUpdate(globalVarKvp.Key, globalVarKvp.Value, (x, y) => globalVarKvp.Value);
            }

            // update the global types dictionary
            foreach (var globalVarKvp in newContents.GlobalTypes)
            {
                gmc.GlobalTypes.AddOrUpdate(globalVarKvp.Key, globalVarKvp.Value, (x, y) => globalVarKvp.Value);
            }

            // Update the module functions dictionary
            foreach (var programFuncKvp in newContents.FunctionDefinitions.Where(x => !x.Value.Private))
            {
                gmc.FunctionDefinitions.AddOrUpdate(programFuncKvp.Key, programFuncKvp.Value, (x, y) => programFuncKvp.Value);
            }

            if (program != null && gmc != null)
            {
                if (Programs.ContainsKey(program))
                    Programs[program] = gmc;
                else
                    Programs.Add(program, gmc);
            }
        }
    }
}
