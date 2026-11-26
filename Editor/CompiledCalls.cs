using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ArtTools.EnvironmentArt
{
    internal static class CompiledCalls
    {
        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static
                                            | BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.DeclaredOnly;

        private const byte TwoByteOpCodePrefix = 0xFE;

        private static readonly Dictionary<short, OperandType> OperandTypes = ReadOperandTypes();
        private static readonly Dictionary<(Module Module, int Token), MethodBase> CalledMethods =
            new Dictionary<(Module Module, int Token), MethodBase>();

        private static Dictionary<short, OperandType> ReadOperandTypes()
        {
            var operandTypes = new Dictionary<short, OperandType>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode opCode) operandTypes[opCode.Value] = opCode.OperandType;
            }

            return operandTypes;
        }

        internal static bool Calls(Type type, Type declaringType, string methodName)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition) type = type.GetGenericTypeDefinition();

            foreach (var method in type.GetMethods(Declared))
                if (BodyCalls(method, declaringType, methodName)) return true;

            foreach (var constructor in type.GetConstructors(Declared))
                if (BodyCalls(constructor, declaringType, methodName)) return true;

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (Calls(nested, declaringType, methodName)) return true;

            return false;
        }

        private static bool BodyCalls(MethodBase method, Type declaringType, string methodName)
        {
            var code = method.GetMethodBody()?.GetILAsByteArray();
            if (code == null) return false;

            int position = 0;
            while (position < code.Length)
            {
                short opCode = code[position++];
                if (opCode == TwoByteOpCodePrefix && position < code.Length)
                    opCode = unchecked((short)((TwoByteOpCodePrefix << 8) | code[position++]));

                if (!OperandTypes.TryGetValue(opCode, out var operand)) return false;

                if (operand == OperandType.InlineMethod)
                {
                    var called = CalledMethod(method, BitConverter.ToInt32(code, position));
                    if (called.Name == methodName && called.DeclaringType == declaringType) return true;
                }

                position += OperandSize(operand, code, position);
            }

            return false;
        }

        private static MethodBase CalledMethod(MethodBase caller, int token)
        {
            var key = (caller.Module, token);
            if (CalledMethods.TryGetValue(key, out var called)) return called;

            var typeArguments = caller.DeclaringType.IsGenericType ? caller.DeclaringType.GetGenericArguments() : null;
            var methodArguments = caller.IsGenericMethod ? caller.GetGenericArguments() : null;

            called = caller.Module.ResolveMethod(token, typeArguments, methodArguments);
            CalledMethods[key] = called;
            return called;
        }

        private static int OperandSize(OperandType operand, byte[] code, int position)
        {
            switch (operand)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return 4 + 4 * BitConverter.ToInt32(code, position);
                default:
                    return 4;
            }
        }
    }
}
