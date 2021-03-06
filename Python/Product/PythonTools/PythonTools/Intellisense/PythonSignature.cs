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
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.PythonTools.Analysis;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Intellisense {
    internal class PythonSignature : ISignature, IOverloadResult {
        private readonly ITrackingSpan _span;
        private readonly string _content, _ppContent;
        private readonly string _documentation;
        private readonly ReadOnlyCollection<IParameter> _parameters;
        private IParameter _currentParameter;
        private readonly IOverloadResult _overload;

        public PythonSignature(ITrackingSpan span, IOverloadResult overload, int paramIndex, string lastKeywordArg = null) {
            _span = span;
            _overload = overload;
            if (lastKeywordArg != null) {
                paramIndex = Int32.MaxValue;
            }

            var content = new StringBuilder(overload.Name);
            var ppContent = new StringBuilder(overload.Name);
            content.Append('(');
            ppContent.AppendLine("(");
            int start = content.Length, ppStart = ppContent.Length;
            var parameters = new IParameter[overload.Parameters.Length];
            for (int i = 0; i < overload.Parameters.Length; i++) {
                ppContent.Append("    ");
                ppStart = ppContent.Length;
                
                var param = overload.Parameters[i];
                if (param.IsOptional) {
                    content.Append('[');
                    ppContent.Append('[');
                }
                if (i > 0) {
                    content.Append(", ");
                    start = content.Length;
                }

                content.Append(param.Name);
                ppContent.Append(param.Name);
                if (!string.IsNullOrEmpty(param.Type) && param.Type != "object") {
                    content.Append(": ");
                    content.Append(param.Type);
                    ppContent.Append(": ");
                    ppContent.Append(param.Type);
                }
                
                if (!String.IsNullOrWhiteSpace(param.DefaultValue)) {
                    content.Append(" = ");
                    content.Append(param.DefaultValue);
                    ppContent.Append(" = ");
                    ppContent.Append(param.DefaultValue);
                }

                var paramSpan = new Span(start, content.Length - start);
                var ppParamSpan = new Span(ppStart, ppContent.Length - ppStart);

                if (param.IsOptional) {
                    content.Append(']');
                    ppContent.Append(']');
                }

                ppContent.AppendLine(",");

                if (lastKeywordArg != null && param.Name == lastKeywordArg) {
                    paramIndex = i;
                }

                parameters[i] = new PythonParameter(this, param, paramSpan, ppParamSpan);
            }
            content.Append(')');
            ppContent.Append(')');

            _content = content.ToString();
            _ppContent = ppContent.ToString();
            _documentation = overload.Documentation.LimitLines(15, stopAtFirstBlankLine: true);

            _parameters = new ReadOnlyCollection<IParameter>(parameters);
            if (lastKeywordArg == null) {
                for (int i = 0; i < parameters.Length; ++i) {
                    if (i == paramIndex || IsParamArray(parameters[i].Name)) {
                        _currentParameter = parameters[i];
                        break;
                    }
                }
            } else if (paramIndex < parameters.Length) {
                _currentParameter = parameters[paramIndex];
            }
        }

        internal static bool IsParamArray(string name) {
            return name != null && name.StartsWith("*") && !name.StartsWith("**");
        }


        internal void SetCurrentParameter(IParameter newValue) {
            if (newValue != _currentParameter) {
                var args = new CurrentParameterChangedEventArgs(_currentParameter, newValue);
                _currentParameter = newValue;
                var changed = CurrentParameterChanged;
                if (changed != null) {
                    changed(this, args);
                }
            }
        }

        public ITrackingSpan ApplicableToSpan {
            get { return _span; }
        }

        public string Content {
            get { return _content; }
        }

        public IParameter CurrentParameter {
            get { return _currentParameter; }
        }

        public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged;

        public string Documentation {
            get { return _documentation; }
        }

        public ReadOnlyCollection<IParameter> Parameters {
            get { return _parameters; }
        }

        #region ISignature Members


        public string PrettyPrintedContent {
            get { return _ppContent; }
        }

        #endregion

        string IOverloadResult.Name {
            get { return _overload.Name; }
        }

        string IOverloadResult.Documentation {
            get { return _documentation; }
        }

        ParameterResult[] IOverloadResult.Parameters {
            get { return _overload.Parameters; }
        }
    }
}
