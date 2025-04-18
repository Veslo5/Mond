﻿using System;
using System.Collections.Generic;
using Mond.Compiler.Expressions;

namespace Mond.Compiler
{
    internal partial class Parser
    {
        private readonly IEnumerator<Token> _tokens;
        private readonly List<Token> _read;
        private Token _previous;

        public Token Previous => _previous;

        public Parser(IEnumerable<Token> tokens)
        {
            _tokens = tokens.GetEnumerator();
            _read = new List<Token>(8);
        }

        /// <summary>
        /// Parse an expression into an expression tree. You can think of expressions as sub-statements.
        /// </summary>
        public Expression ParseExpression(int precendence = 0)
        {
            var token = Take();

            _prefixParselets.TryGetValue(token.Type, out var prefixParselet);
            if (prefixParselet == null)
                throw new MondCompilerException(token, CompilerError.ExpectedButFound, "Expression", token);

            var left = prefixParselet.Parse(this, token);
            left.EndToken = _previous;

            while (GetPrecedence() > precendence) // swapped because resharper
            {
                token = Take();

                _infixParselets.TryGetValue(token.Type, out var infixParselet);
                if (infixParselet == null)
                    throw new Exception("probably can't happen");

                left = infixParselet.Parse(this, left, token);
                left.EndToken = _previous;
            }

            return left;
        }

        /// <summary>
        /// Parse a statement into an expression tree.
        /// </summary>
        public Expression ParseStatement(bool takeTrailingSemicolon = true)
        {
            var token = Peek();

            _statementParselets.TryGetValue(token.Type, out var statementParselet);

            Expression result;

            if (statementParselet == null)
            {
                result = ParseExpression();

                if (takeTrailingSemicolon)
                    Take(TokenType.Semicolon);

                result.EndToken = _previous;
                return result;
            }

            token = Take();

            result = statementParselet.Parse(this, token, out var hasTrailingSemicolon);

            if (takeTrailingSemicolon && hasTrailingSemicolon)
                Take(TokenType.Semicolon);

            result.EndToken = _previous;
            return result;
        }

        /// <summary>
        /// Parse a block of code into an expression tree. Blocks can either be a single statement or 
        /// multiple surrounded by braces.
        /// </summary>
        public BlockExpression ParseBlock(bool allowSingle = true)
        {
            var statements = new List<Expression>();

            if (allowSingle && !Match(TokenType.LeftBrace))
            {
                statements.Add(ParseStatement());

                return new BlockExpression(statements)
                {
                    EndToken = _previous
                };
            }

            var openBrace = Take(TokenType.LeftBrace);

            while (!Match(TokenType.RightBrace))
            {
                statements.Add(ParseStatement());
            }

            Take(TokenType.RightBrace);

            if (statements.Count == 0)
                statements.Add(new EmptyExpression(openBrace));

            return new BlockExpression(openBrace, statements)
            {
                EndToken = _previous
            };
        }

        /// <summary>
        /// Parses statements until there are no more tokens available.
        /// </summary>
        public Expression ParseAll()
        {
            var statements = new List<Expression>();

            while (!Match(TokenType.Eof))
            {
                statements.Add(ParseStatement());
            }

            return new BlockExpression(statements)
            {
                EndToken = _previous
            };
        }

        /// <summary>
        /// Check if the next token matches the given type. If they match, take the token.
        /// </summary>
        public bool MatchAndTake(TokenType type) => MatchAndTake(type, out _);

        /// <summary>
        /// Check if the next token matches the given type. If they match, take the token.
        /// </summary>
        public bool MatchAndTake(TokenType type, out Token token)
        {
            var isMatch = Match(type);
            token = isMatch ? Take() : null;
            return isMatch;
        }

        /// <summary>
        /// Check if the next token matches the given subtype. If they match, take the token.
        /// </summary>
        public bool MatchAndTake(TokenSubType subType)
        {
            var isMatch = Match(subType);
            if (isMatch)
                Take();

            return isMatch;
        }

        /// <summary>
        /// Check if the next token matches the given type.
        /// </summary>
        public bool Match(TokenType type, int distance = 0)
        {
            return Peek(distance).Type == type;
        }

        /// <summary>
        /// Check if the next token matches the given subtype.
        /// </summary>
        public bool Match(TokenSubType subType, int distance = 0)
        {
            return Peek(distance).SubType == subType;
        }

        /// <summary>
        /// Take a token from the stream. Throws an exception if the given type does not match the token type.
        /// </summary>
        public Token Take(TokenType type)
        {
            var token = Take();

            if (token.Type != type)
                throw new MondCompilerException(token, CompilerError.ExpectedButFound, type, token);

            return token;
        }

        /// <summary>
        /// Take a token from the stream. Throws an exception if the given subtype does not match the token subtype.
        /// </summary>
        public Token Take(TokenSubType subType)
        {
            var token = Take();

            if (token.SubType != subType)
                throw new MondCompilerException(token, CompilerError.ExpectedButFound, subType, token);

            return token;
        }

        /// <summary>
        /// Take a token from the stream.
        /// </summary>
        public Token Take()
        {
            Peek();

            var result = _read[0];
            _read.RemoveAt(0);

            _previous = result;

            return result;
        }

        /// <summary>
        /// Peek at future tokens in the stream. Distance is the number of tokens from the current one.
        /// </summary>
        public Token Peek(int distance = 0)
        {
            if (distance < 0)
                throw new ArgumentOutOfRangeException("distance", "distance can't be negative");

            while (_read.Count <= distance)
            {
                _tokens.MoveNext();
                _read.Add(_tokens.Current);

                //Console.WriteLine(_tokens.Current.Type);
            }

            return _read[distance];
        }

        private int GetPrecedence()
        {
            _infixParselets.TryGetValue(Peek().Type, out var infixParselet);
            return infixParselet != null ? infixParselet.Precedence : 0;
        }
    }
}
