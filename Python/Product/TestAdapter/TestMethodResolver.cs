// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.TestAdapter;
using MSBuild = Microsoft.Build.Evaluation;

namespace Microsoft.PythonTools.TestAdapter {
    [Export(typeof(ITestMethodResolver))]
    class TestMethodResolver : ITestMethodResolver {
        private readonly IServiceProvider _serviceProvider;
        private readonly TestContainerDiscoverer _discoverer;
        private readonly IInterpreterOptionsService _interpreterService;

        #region ITestMethodResolver Members

        [ImportingConstructor]
        public TestMethodResolver([Import(typeof(SVsServiceProvider))]IServiceProvider serviceProvider,
            [Import]TestContainerDiscoverer discoverer) {
            _serviceProvider = serviceProvider;
            _discoverer = discoverer;
            _interpreterService = ((IComponentModel)_serviceProvider.GetService(typeof(SComponentModel))).GetService<IInterpreterOptionsService>();
        }

        public Uri ExecutorUri {
            get { return TestExecutor.ExecutorUri; }
        }

        public string GetCurrentTest(string filePath, int line, int lineCharOffset) {
            var project = PathToProject(filePath);
            if (project != null && _discoverer.IsProjectKnown(project)) {
                var buildEngine = new MSBuild.ProjectCollection();
                string projectPath;
                if (project.TryGetProjectPath(out projectPath)) {
                    var proj = buildEngine.LoadProject(projectPath);

                    var provider = new MSBuildProjectInterpreterFactoryProvider(_interpreterService, proj);
                    try {
                        provider.DiscoverInterpreters();
                    } catch (InvalidDataException) {
                        // This exception can be safely ignored here.
                    }
                    var factory = provider.ActiveInterpreter;

                    var parser = Parser.CreateParser(
                        new StreamReader(filePath),
                        factory.GetLanguageVersion()
                    );
                    var ast = parser.ParseFile();
                    var walker = new FunctionFinder(ast, line, lineCharOffset);
                    ast.Walk(walker);
                    var projHome = Path.GetFullPath(Path.Combine(proj.DirectoryPath, proj.GetPropertyValue(PythonConstants.ProjectHomeSetting) ?? "."));

                    if (walker.ClassName != null && walker.FunctionName != null) {
                        return TestAnalyzer.MakeFullyQualifiedTestName(
                            CommonUtils.CreateFriendlyFilePath(projHome, filePath),
                            walker.ClassName,
                            walker.FunctionName
                        );
                    }
                }
            }
            return null;
        }

        class FunctionFinder : PythonWalker {
            public string ClassName, FunctionName;
            private readonly PythonAst _root;
            private readonly int _line, _lineOffset;

            public FunctionFinder(PythonAst root, int line, int lineOffset) {
                _root = root;
                _line = line;
                _lineOffset = lineOffset;
            }

            public override bool Walk(ClassDefinition node) {
                if (FunctionName == null) {
                    ClassName = node.Name;
                }
                return base.Walk(node);
            }

            public override bool Walk(FunctionDefinition node) {
                var start = node.GetStart(_root);
                var end = node.GetEnd(_root);
                if (start.Line <= _line && end.Line >= _line) {
                    FunctionName = node.Name;
                }

                return base.Walk(node);
            }
        }

        private IVsProject PathToProject(string filePath) {
            var rdt = (IVsRunningDocumentTable)_serviceProvider.GetService(typeof(SVsRunningDocumentTable));
            IVsHierarchy hierarchy;
            uint itemId;
            IntPtr docData = IntPtr.Zero;
            uint cookie;
            try {
                var hr = rdt.FindAndLockDocument(
                    (uint)_VSRDTFLAGS.RDT_NoLock,
                    filePath,
                    out hierarchy,
                    out itemId,
                    out docData,
                    out cookie);
                ErrorHandler.ThrowOnFailure(hr);
            } finally {
                if (docData != IntPtr.Zero) {
                    Marshal.Release(docData);
                    docData = IntPtr.Zero;
                }
            }

            return hierarchy as IVsProject;
        }

        #endregion
    }
}
