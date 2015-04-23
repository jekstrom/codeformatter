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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    internal partial class MethodNamingRule
    {
        private sealed class CSharpRule : CommonRule
        {
            protected override SyntaxNode AddMethodAnnotations(SyntaxNode syntaxNode, out int count)
            {
                return CSharpMethodAnnotationsRewriter.AddAnnotations(syntaxNode, out count);
            }

            protected override SyntaxNode RemoveRenameAnnotations(SyntaxNode syntaxNode)
            {
                var rewriter = new CSharpRemoveRenameAnnotationsRewriter();
                return rewriter.Visit(syntaxNode);
            }
        }

        /// <summary>
        /// This will add an annotation to any private field that needs to be renamed.
        /// </summary>
        internal sealed class CSharpMethodAnnotationsRewriter : CSharpSyntaxRewriter
        {
            private int _count;

            internal static SyntaxNode AddAnnotations(SyntaxNode node, out int count)
            {
                var rewriter = new CSharpMethodAnnotationsRewriter();
                var newNode = rewriter.Visit(node);
                count = rewriter._count;
                return newNode;
            }

			public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
				if (NeedsRewrite(node)) 
				{
					_count++;

					return node.WithAdditionalAnnotations(s_markerAnnotation);
				}

				return node;
			}

            private static bool NeedsRewrite(MethodDeclarationSyntax methodSyntax)
            {
				string methodName = methodSyntax.Identifier.ValueText;
				return !String.IsNullOrWhiteSpace(methodName) && methodName.Length > 1 && char.IsLower(methodName[0]);
            }
        }

        /// <summary>
        /// This rewriter exists to work around DevDiv 1086632 in Roslyn.  The Rename action is 
        /// leaving a set of annotations in the tree.  These annotations slow down further processing
        /// and eventually make the rename operation unusable.  As a temporary work around we manually
        /// remove these from the tree.
        /// </summary>
        private sealed class CSharpRemoveRenameAnnotationsRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode Visit(SyntaxNode node)
            {
                node = base.Visit(node);
                if (node != null && node.ContainsAnnotations && node.GetAnnotations(s_renameAnnotationName).Any())
                {
                    node = node.WithoutAnnotations(s_renameAnnotationName);
                }

                return node;
            }

            public override SyntaxToken VisitToken(SyntaxToken token)
            {
                token = base.VisitToken(token);
                if (token.ContainsAnnotations && token.GetAnnotations(s_renameAnnotationName).Any())
                {
                    token = token.WithoutAnnotations(s_renameAnnotationName);
                }

                return token;
            }
        }
    }
}
