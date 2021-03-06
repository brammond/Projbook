﻿using EnsureThat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Projbook.Core.Exception;
using Projbook.Core.Projbook.Core.Snippet.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Projbook.Core.Snippet.CSharp
{
    /// <summary>
    /// Extractor in charge of browsing source directories. load file content and extract requested member.
    /// </summary>
    public class CSharpSnippetExtractor : DefaultSnippetExtractor
    {
        /// <summary>
        /// Represents the matching trie used for member matching.
        /// Because of the cost of building the Trie, this value is lazy loaded and kept for future usages.
        /// </summary>
        private CSharpSyntaxMatchingNode syntaxTrie;

        /// <summary>
        /// Initializes a new instance of <see cref="CSharpSnippetExtractor"/>.
        /// </summary>
        /// <param name="sourceDirectories">Initializes the required <see cref="SourceDictionaries"/>.</param>
        public CSharpSnippetExtractor(params DirectoryInfo[] sourceDirectories)
            : base (sourceDirectories)
        {
        }

        /// <summary>
        /// Extracts a snippet from a given rule pattern.
        /// </summary>
        /// <param name="memberPattern">The mem.</param>
        /// <returns>The extracted snippet.</returns>
        public override Model.Snippet Extract(string filePath, string memberPattern)
        {
            // Return the entire code if no member is specified
            if (string.IsNullOrWhiteSpace(memberPattern))
            {
                return base.Extract(filePath, memberPattern);
            }

            // Parse the matching rule from the pattern
            CSharpMatchingRule rule = CSharpMatchingRule.Parse(memberPattern);

            // Load the trie for pattern matching
            if (null == this.syntaxTrie)
            {
                // Load file content
                string sourceCode = base.LoadFile(filePath);

                // Build a syntax tree from the source code
                SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
                SyntaxNode root = tree.GetRoot();

                // Visit the syntax tree for generating a Trie for pattern matching
                CSharpSyntaxWalkerMatchingBuilder syntaxMatchingBuilder = new CSharpSyntaxWalkerMatchingBuilder();
                syntaxMatchingBuilder.Visit(root);

                // Retrieve the Trie root
                this.syntaxTrie = syntaxMatchingBuilder.Root;
            }

            // Match the rule from the syntax matching Trie
            CSharpSyntaxMatchingNode matchingTrie = syntaxTrie.Match(rule.MatchingChunks);
            if (null == matchingTrie)
            {
                throw new SnippetExtractionException("Cannot find member", string.Format("{0} {1}", filePath, memberPattern));
            }

            // Build a snippet for extracted syntax nodes
            return this.BuildSnippet(matchingTrie.MatchingSyntaxNodes, rule.ExtractionMode);
        }

        /// <summary>
        /// Builds a snippet from extracted syntax nodes.
        /// </summary>
        /// <param name="nodes">The exctracted nodes.</param>
        /// <param name="extractionMode">The extraction mode.</param>
        /// <returns>The built snippet.</returns>
        private Model.Snippet BuildSnippet(SyntaxNode[] nodes, CSharpExtractionMode extractionMode)
        {
            // Data validation
            Ensure.That(() => nodes).IsNotNull();
            Ensure.That(() => nodes).HasItems();

            // Extract code from each snippets
            StringBuilder stringBuilder = new StringBuilder();
            bool firstSnippet = true;
            foreach (SyntaxNode node in nodes)
            {
                // Write line return between each snippet
                if (!firstSnippet)
                {
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine();
                }
                
                // Write each snippet line
                string[] lines = node.GetText().Lines.Select(x => x.ToString()).ToArray();
                this.WriteAndCleanupSnippet(stringBuilder, lines, extractionMode);

                // Flag the first snippet as false
                firstSnippet = false;
            }
            
            // Create the snippet from the exctracted code
            return new Model.Snippet(stringBuilder.ToString());
        }

        /// <summary>
        /// Writes and cleanup line snippets.
        /// Snippets are moved out of their context, for this reasong we need to trim lines aroung and remove a part of the indentation.
        /// </summary>
        /// <param name="stringBuilder">The string builder used as output.</param>
        /// <param name="lines">The lines to process.</param>
        /// <param name="extractionMode">The extraction mode.</param>
        private void WriteAndCleanupSnippet(StringBuilder stringBuilder, string[] lines, CSharpExtractionMode extractionMode)
        {
            // Data validation
            Ensure.That(() => stringBuilder).IsNotNull();
            Ensure.That(() => lines).IsNotNull();

            // Do not process if lines are empty
            if (0 >= lines.Length)
            {
                return;
            }

            // Compute the index of the first selected line
            int startPos = 0;
            if (CSharpExtractionMode.ContentOnly == extractionMode)
            {
                for (; startPos < lines.Length && !lines[startPos].ToString().Contains('{'); ++startPos);
                
                // Extract block code if any opening bracket has been found
                if (startPos < lines.Length)
                {
                    int openingBracketPos = lines[startPos].IndexOf('{');
                    if (openingBracketPos >= 0)
                    {
                        // Extract the code before the curly bracket
                        if (lines[startPos].Length > openingBracketPos)
                        {
                            lines[startPos] = lines[startPos].Substring(openingBracketPos + 1);
                        }

                        // Skip the current line if empty
                        if (string.IsNullOrWhiteSpace(lines[startPos]) && lines.Length > 1 + startPos)
                        {
                            ++startPos;
                        }
                    }
                }
            }
            else
            {
                for (; startPos < lines.Length && lines[startPos].ToString().Trim().Length == 0; ++startPos);
            }

            // Compute the index of the lastselected line
            int endPos = -1 + lines.Length;
            if (CSharpExtractionMode.ContentOnly == extractionMode)
            {
                for (; 0 <= endPos && !lines[endPos].ToString().Contains('}'); --endPos);

                // Extract block code if any closing bracket has been found
                if (0 <= endPos)
                {
                    int closingBracketPos = lines[endPos].IndexOf('}');
                    if (closingBracketPos >= 0)
                    {
                        // Extract the code before the curly bracket
                        if (lines[endPos].Length > closingBracketPos)
                            lines[endPos] = lines[endPos].Substring(0, closingBracketPos).TrimEnd();
                    }

                    // Skip the current line if empty
                    if (string.IsNullOrWhiteSpace(lines[endPos]) && lines.Length > -1 + endPos)
                    {
                        --endPos;
                    }
                }
            }
            else
            {
                for (; 0 <= endPos && lines[endPos].ToString().Trim().Length == 0; --endPos) ;
            }

            // Compute the padding to remove for removing a part of the indentation
            int leftPadding = int.MaxValue;
            for (int i = startPos; i <= endPos; ++i)
            {
                // Ignore empty lines in the middle of the snippet
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    // Adjust the left padding with the available whitespace at the beginning of the line
                    leftPadding = Math.Min(leftPadding, lines[i].ToString().TakeWhile(Char.IsWhiteSpace).Count());
                }
            }

            // Write selected lines to the string builder
            bool firstLine = true;
            for (int i = startPos; i <= endPos; ++i)
            {
                // Write line return between each line
                if (!firstLine)
                {
                    stringBuilder.AppendLine();
                }

                // Remove a part of the indentation padding
                if (lines[i].Length > leftPadding)
                {
                    string line = lines[i].Substring(leftPadding);

                    // Process the snippet depending on the extraction mode
                    switch (extractionMode)
                    {
                        // Extract the block structure only
                        case CSharpExtractionMode.BlockStructureOnly:
                            int openingBracketPos = line.IndexOf('{');
                            if (openingBracketPos >= 0)
                            {
                                // Extract the code before the curly bracket
                                if (line.Length > openingBracketPos)
                                    line = line.Substring(0, 1 + openingBracketPos);

                                // Replace the content and close the block
                                line += string.Format("{0}    // ...{0}}}", Environment.NewLine);

                                // Stop the iteration
                                endPos = i;
                            }
                            break;
                    }

                    // Append the line
                    stringBuilder.Append(line);
                }
                
                // Flag the first line as false
                firstLine = false;
            }
        }
    }
}