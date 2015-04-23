// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    [GlobalSemanticRuleOrder(GlobalSemanticRuleOrder.StaticNamingRule)]
    internal partial class StaticNamingRule : IGlobalSemanticFormattingRule
    {
        #region CommonRule

        private abstract class CommonRule
        {
            protected abstract SyntaxNode AddMethodAnnotations(SyntaxNode syntaxNode, out int count);

            /// <summary>
            /// This method exists to work around DevDiv 1086632 in Roslyn.  The Rename action is 
            /// leaving a set of annotations in the tree.  These annotations slow down further processing
            /// and eventually make the rename operation unusable.  As a temporary work around we manually
            /// remove these from the tree.
            /// </summary>
            protected abstract SyntaxNode RemoveRenameAnnotations(SyntaxNode syntaxNode);

            public async Task<Solution> ProcessAsync(Document document, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
            {
                int count;
                var newSyntaxRoot = AddMethodAnnotations(syntaxRoot, out count);

                if (count == 0)
                {
                    return document.Project.Solution;
                }

                var documentId = document.Id;
                var solution = document.Project.Solution;
                solution = solution.WithDocumentSyntaxRoot(documentId, newSyntaxRoot);
                solution = await RenameStatics(solution, documentId, count, cancellationToken);
                return solution;
            }

            private async Task<Solution> RenameStatics(Solution solution, DocumentId documentId, int count, CancellationToken cancellationToken)
            {
                Solution oldSolution = null;
                for (int i = 0; i < count; i++)
                {
                    oldSolution = solution;

                    var semanticModel = await solution.GetDocument(documentId).GetSemanticModelAsync(cancellationToken);
                    var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken);
                    var staticNode = root.GetAnnotatedNodes(s_markerAnnotation).ElementAt(i);
					var symbol = semanticModel.GetSymbolInfo(staticNode, cancellationToken);
					var staticSymbol = symbol.Symbol.ContainingSymbol;

					var d = root.FindToken(staticNode.Span.Start).Parent.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().First();
					var stringIdentifier = SyntaxFactory.PredefinedType(d.Expression.GetFirstToken()).WithAdditionalAnnotations(s_markerAnnotation);
					var newTypeName = d.ReplaceNode(d.Expression, stringIdentifier);
					var newRoot = root.ReplaceNode(d, newTypeName);



                    var newName = GetNewStaticName(staticSymbol);

                    if (newName == staticSymbol.Name)
                    {
                        continue;
                    }
					
                    solution = await Renamer.RenameSymbolAsync(solution, staticSymbol, newName, solution.Workspace.Options, cancellationToken).ConfigureAwait(false);
                    solution = await CleanSolutionAsync(solution, oldSolution, cancellationToken);
                }

                return solution;
            }

            private static string GetNewStaticName(ISymbol symbol)
            {
				string name = symbol.Name;
                if (symbol.Kind == SymbolKind.NamedType && char.IsLower(name[0])) 
				{
					//name = char.ToUpper(name[0]) + name.Substring(1);
					name = symbol.ContainingType.Name;
				}

                return name;
            }

            private async Task<Solution> CleanSolutionAsync(Solution newSolution, Solution oldSolution, CancellationToken cancellationToken)
            {
                var solution = newSolution;

                foreach (var projectChange in newSolution.GetChanges(oldSolution).GetProjectChanges())
                {
                    foreach (var documentId in projectChange.GetChangedDocuments())
                    {
                        solution = await CleanSolutionDocument(solution, documentId, cancellationToken);
                    }
                }

                return solution;
            }

            private async Task<Solution> CleanSolutionDocument(Solution solution, DocumentId documentId, CancellationToken cancellationToken)
            {
                var document = solution.GetDocument(documentId);
                var syntaxNode = await document.GetSyntaxRootAsync(cancellationToken);
                if (syntaxNode == null)
                {
                    return solution;
                }

                var newNode = RemoveRenameAnnotations(syntaxNode);
                return solution.WithDocumentSyntaxRoot(documentId, newNode);
            }
        }

        #endregion

        private const string s_renameAnnotationName = "Rename";

        private readonly static SyntaxAnnotation s_markerAnnotation = new SyntaxAnnotation("StaticToRename");

        // Used to avoid the array allocation on calls to WithAdditionalAnnotations
        private readonly static SyntaxAnnotation[] s_markerAnnotationArray;

        static StaticNamingRule()
        {
            s_markerAnnotationArray = new[] { s_markerAnnotation };
        }

        private readonly CSharpRule _csharpRule = new CSharpRule();

        public bool SupportsLanguage(string languageName)
        {
            return
                languageName == LanguageNames.CSharp ||
                languageName == LanguageNames.VisualBasic;
        }

        public Task<Solution> ProcessAsync(Document document, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
        {
            switch (document.Project.Language)
            {
                case LanguageNames.CSharp:
                    return _csharpRule.ProcessAsync(document, syntaxRoot, cancellationToken);
                case LanguageNames.VisualBasic:
					throw new NotSupportedException();
				default:
                    throw new NotSupportedException();
            }
        }

        private static bool IsGoodMethodName(string name)
        {
			return name.Length > 1 && char.IsUpper(name[0]);
        }
    }
}
