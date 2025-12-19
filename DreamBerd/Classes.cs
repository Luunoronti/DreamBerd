using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    internal sealed class ClassDefinition
    {
        public string Name { get; }
        public Dictionary<string, Evaluator.FunctionDefinition> Methods { get; }

        public ClassDefinition(string name, Dictionary<string, Evaluator.FunctionDefinition> methods)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        }
    }

    public sealed class ClassInstance
    {
        internal ClassInstance(ClassDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Fields = new Dictionary<string, Value>(StringComparer.Ordinal);
        }

        internal ClassDefinition Definition { get; }
        internal Dictionary<string, Value> Fields { get; }
        public bool Initialized { get; set; }
        public string Name => Definition.Name;
    }

    public sealed class BoundMethod
    {
        internal BoundMethod(ClassInstance target, string name, Evaluator.FunctionDefinition definition)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ClassInstance Target { get; }
        public string Name { get; }
        internal Evaluator.FunctionDefinition Definition { get; }
    }

    internal sealed class FieldHistory
    {
        public List<Value> Values { get; } = new();
        public int Index;
    }
}
