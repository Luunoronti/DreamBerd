// Context.cs
using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public sealed class Context
    {
        private sealed class Binding
        {
            public Mutability Mutability;
            public Value Value;
            public int Priority;
        }

        private readonly Dictionary<string, Binding> _variables =
            new Dictionary<string, Binding>(StringComparer.Ordinal);

        public void DeclareVariable(string name, Mutability mutability, Value initialValue, int priority)
        {
            if (_variables.TryGetValue(name, out var existing))
            {
                // prosty model: wygrywa większy priorytet
                if (priority < existing.Priority)
                    return;
            }

            _variables[name] = new Binding
            {
                Mutability = mutability,
                Value = initialValue,
                Priority = priority
            };
        }

        public Value GetVariable(string name)
        {
            if (!_variables.TryGetValue(name, out var binding))
                throw new InterpreterException($"Variable '{name}' is not defined.");

            return binding.Value;
        }

        public bool TryGetVariable(string name, out Value value)
        {
            if (_variables.TryGetValue(name, out var binding))
            {
                value = binding.Value;
                return true;
            }

            value = default;
            return false;
        }

        public void AssignVariable(string name, Value newValue)
        {
            if (!_variables.TryGetValue(name, out var binding))
                throw new InterpreterException($"Variable '{name}' is not defined.");

            if (binding.Mutability == Mutability.ConstConst || binding.Mutability == Mutability.ConstVar)
                throw new InterpreterException($"Variable '{name}' is not assignable.");

            binding.Value = newValue;
        }

        public void DeleteVariable(string name)
        {
            _variables.Remove(name);
        }
    }
}
