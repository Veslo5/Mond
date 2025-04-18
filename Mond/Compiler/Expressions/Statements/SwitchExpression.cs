﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mond.Compiler.Expressions.Statements
{
    class SwitchExpression : Expression
    {
        public class Branch
        {
            public ReadOnlyCollection<Expression> Conditions { get; }
            public ScopeExpression Block { get; }

            public Branch(List<Expression> conditions, ScopeExpression block)
            {
                Conditions = conditions?.AsReadOnly();
                Block = block;
            }
        }

        public Expression Expression { get; private set; }
        public ReadOnlyCollection<Branch> Branches { get; private set; }

        public SwitchExpression(Token token, Expression expression, List<Branch> branches)
            : base(token)
        {
            Expression = expression;
            Branches = branches.AsReadOnly();
        }

        public override int Compile(FunctionContext context)
        {
            context.Position(Token);

            var stack = 0;
            var caseLabels = new List<LabelOperand>(Branches.Count);

            var caseEnd = context.MakeLabel("caseEnd");
            LabelOperand caseDefault = null;
            BlockExpression defaultBlock = null;

            for (var i = 0; i < Branches.Count; i++)
            {
                var label = context.MakeLabel("caseBranch_" + i);
                caseLabels.Add(label);

                var conditions = Branches[i].Conditions;
                if (conditions.Any(c => c == null))
                {
                    caseDefault = label;

                    if (conditions.Count == 1)
                        defaultBlock = Branches[i].Block;
                }
            }

            var emptyDefault = caseDefault == null;
            if (emptyDefault)
                caseDefault = context.MakeLabel("caseDefault");

            context.Statement(Expression);
            stack += Expression.Compile(context);
            
            var flattenedBranches = FlattenBranches(Branches, caseLabels, caseDefault);
            BuildTables(flattenedBranches, caseDefault, out var tables, out var rest);

            foreach (var table in tables)
            {
                var start = table.Entries[0].Value;
                var labels = table.Entries.Select(e => e.Label).ToList();

                stack += context.Dup();
                stack += context.JumpTable(start, labels);
            }

            foreach (var entry in rest)
            {
                stack += context.Dup();
                stack += entry.Condition.Compile(context);
                stack += context.BinaryOperation(TokenType.EqualTo);
                stack += context.JumpTrue(entry.Label);
            }

            stack += context.Jump(caseDefault);

            context.PushLoop(null, caseEnd);

            for (var i = 0; i < Branches.Count; i++)
            {
                var branchStack = stack;
                var branch = Branches[i];

                if (defaultBlock != null && branch.Block == defaultBlock)
                    branchStack += context.Bind(caseDefault);

                branchStack += context.Bind(caseLabels[i]);
                branchStack += context.Drop();
                branchStack += branch.Block.Compile(context);
                branchStack += context.Jump(caseEnd);

                CheckStack(branchStack, 0);
            }

            // only bind if we have no default block
            if (emptyDefault)
                stack += context.Bind(caseDefault);

            // always drop the switch value
            stack += context.Drop();

            context.PopLoop();

            stack += context.Bind(caseEnd);

            CheckStack(stack, 0);
            return stack;
        }

        public override Expression Simplify(SimplifyContext context)
        {
            Expression = Expression.Simplify(context);

            Branches = Branches
                .Select(b =>
                {
                    var conditions = b.Conditions
                        .Select(c => c?.Simplify(context))
                        .ToList();

                    return new Branch(conditions, (ScopeExpression)b.Block.Simplify(context));
                })
                .ToList()
                .AsReadOnly();

            return this;
        }

        public override void SetParent(Expression parent)
        {
            base.SetParent(parent);

            Expression.SetParent(this);

            foreach (var branch in Branches)
            {
                foreach (var condition in branch.Conditions)
                {
                    condition?.SetParent(this);
                }

                branch.Block.SetParent(this);
            }
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        #region Jump Table Stuff
        private class JumpEntry
        {
            public Expression Condition { get; }
            public LabelOperand Label { get; }

            public JumpEntry(Expression condition, LabelOperand label)
            {
                Condition = condition;
                Label = label;
            }
        }

        private class JumpTableEntry<T>
        {
            public Expression Condition { get; }
            public T Value { get; }
            public LabelOperand Label { get; }

            public JumpTableEntry(Expression condition, T value, LabelOperand label)
            {
                Condition = condition;
                Value = value;
                Label = label;
            }
        }

        private class JumpTable
        {
            public ReadOnlyCollection<JumpTableEntry<int>> Entries { get; }
            public int Holes { get; }

            public JumpTable(List<JumpTableEntry<int>> entries, int holes)
            {
                Entries = entries.AsReadOnly();
                Holes = holes;
            }
        }

        static IEnumerable<JumpEntry> FlattenBranches(IList<Branch> branches, IList<LabelOperand> labels, LabelOperand defaultLabel)
        {
            var branchConditions = new HashSet<MondValue>();

            for (var i = 0; i < branches.Count; i++)
            {
                foreach (var condition in branches[i].Conditions)
                {
                    if (condition == null) // default
                    {
                        yield return new JumpEntry(null, defaultLabel);
                        continue;
                    }

                    var constantExpression = condition as IConstantExpression;
                    if (constantExpression == null)
                        throw new MondCompilerException(condition, CompilerError.ExpectedConstant);

                    if (!branchConditions.Add(constantExpression.GetValue()))
                        throw new MondCompilerException(condition, CompilerError.DuplicateCase);

                    yield return new JumpEntry(condition, labels[i]);
                }
            }
        }

        static void BuildTables(IEnumerable<JumpEntry> jumps, LabelOperand defaultLabel, out List<JumpTable> tables, out List<JumpEntry> rest)
        {
            rest = new List<JumpEntry>();

            var numbers = FilterJumps(jumps, rest);

            var comparer = new GenericComparer<JumpTableEntry<int>>((b1, b2) => b1.Value - b2.Value);
            numbers.Sort(comparer);

            tables = new List<JumpTable>();

            for (var i = 0; i < numbers.Count; i++)
            {
                var table = TryBuildTable(numbers, i, defaultLabel);

                if (table != null)
                {
                    tables.Add(table);
                    i += table.Entries.Count - table.Holes - 1;
                }
                else
                {
                    rest.Add(new JumpEntry(numbers[i].Condition, numbers[i].Label));
                }
            }
        }

        static List<JumpTableEntry<int>> FilterJumps(IEnumerable<JumpEntry> jumps, ICollection<JumpEntry> rest)
        {
            var numbers = new List<JumpTableEntry<int>>();

            foreach (var jump in jumps)
            {
                var condition = jump.Condition;

                if (condition == null) // default
                    continue;

                var numberExpression = condition as NumberExpression;
                if (numberExpression == null)
                {
                    rest.Add(jump);
                    continue;
                }

                var number = numberExpression.Value;
                if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number - (int)number) > double.Epsilon)
                {
                    rest.Add(jump);
                    continue;
                }

                numbers.Add(new JumpTableEntry<int>(jump.Condition, (int)number, jump.Label));
            }

            return numbers;
        }

        static JumpTable TryBuildTable(IList<JumpTableEntry<int>> jumps, int offset, LabelOperand defaultLabel)
        {
            var tableEntries = new List<JumpTableEntry<int>>();
            var tableHoles = 0;

            var prev = jumps[offset].Value;
            for (var i = offset; i < jumps.Count; i++)
            {
                var holeSize = jumps[i].Value - prev;
                if (holeSize < 0) throw new Exception("not sorted");

                holeSize--;

                if (holeSize > 3)
                    break;

                for (var j = 0; j < holeSize; j++)
                {
                    tableEntries.Add(new JumpTableEntry<int>(null, 0, defaultLabel));
                }

                tableEntries.Add(jumps[i]);

                tableHoles += Math.Max(holeSize, 0);
                prev = jumps[i].Value;
            }

            if (tableEntries.Count < 3)
                return null;

            if ((double)tableHoles / tableEntries.Count >= 0.25) // TODO: allow more holes for large tables?
                return null;

            return new JumpTable(tableEntries, tableHoles);
        }
        #endregion
    }
}
