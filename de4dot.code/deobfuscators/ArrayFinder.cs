﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators {
	static class ArrayFinder {
		public static List<byte[]> getArrays(MethodDefinition method) {
			return getArrays(method, null);
		}

		public static List<byte[]> getArrays(MethodDefinition method, TypeReference arrayElemntType) {
			var arrays = new List<byte[]>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				TypeReference type;
				var ary = getArray(instrs, ref i, out type);
				if (ary == null)
					break;
				if (arrayElemntType != null && !MemberReferenceHelper.compareTypes(type, arrayElemntType))
					continue;

				arrays.Add(ary);
			}
			return arrays;
		}

		public static byte[] getArray(IList<Instruction> instrs, ref int index, out TypeReference type) {
			for (int i = index; i < instrs.Count - 2; i++) {
				var newarr = instrs[i++];
				if (newarr.OpCode.Code != Code.Newarr)
					continue;

				if (instrs[i++].OpCode.Code != Code.Dup)
					continue;

				var ldtoken = instrs[i++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null)
					continue;

				index = i - 3;
				type = newarr.Operand as TypeReference;
				return field.InitialValue;
			}

			index = instrs.Count;
			type = null;
			return null;
		}

		public static byte[] getInitializedByteArray(MethodDefinition method, int arraySize) {
			int newarrIndex = findNewarr(method, arraySize);
			if (newarrIndex < 0)
				return null;
			return getInitializedByteArray(arraySize, method, ref newarrIndex);
		}

		public static byte[] getInitializedByteArray(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultValueArray = getInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I1);

			var resultArray = new byte[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (byte)intValue.value;
			}
			return resultArray;
		}

		public static short[] getInitializedInt16Array(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultValueArray = getInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I2);

			var resultArray = new short[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (short)intValue.value;
			}
			return resultArray;
		}

		public static int[] getInitializedInt32Array(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultValueArray = getInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I4);

			var resultArray = new int[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (int)intValue.value;
			}
			return resultArray;
		}

		public static uint[] getInitializedUInt32Array(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultArray = getInitializedInt32Array(arraySize, method, ref newarrIndex);
			if (resultArray == null)
				return null;

			var ary = new uint[resultArray.Length];
			for (int i = 0; i < ary.Length; i++)
				ary[i] = (uint)resultArray[i];
			return ary;
		}

		public static Value[] getInitializedArray(int arraySize, MethodDefinition method, ref int newarrIndex, Code stelemOpCode) {
			var resultValueArray = new Value[arraySize];

			var emulator = new InstructionEmulator(method);
			var theArray = new UnknownValue();
			emulator.push(theArray);

			var instructions = method.Body.Instructions;
			int i;
			for (i = newarrIndex + 1; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (instr.OpCode.Code == Code.Newarr)
					break;
				switch (instr.OpCode.Code) {
				case Code.Newarr:
				case Code.Newobj:
					goto done;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Starg:
				case Code.Starg_S:
				case Code.Stsfld:
				case Code.Stfld:
					if (emulator.peek() == theArray && i != newarrIndex + 1 && i != newarrIndex + 2)
						goto done;
					break;
				}

				if (instr.OpCode.Code == stelemOpCode) {
					var value = emulator.pop();
					var index = emulator.pop() as Int32Value;
					var array = emulator.pop();
					if (ReferenceEquals(array, theArray) && index != null && index.allBitsValid()) {
						if (0 <= index.value && index.value < resultValueArray.Length)
							resultValueArray[index.value] = value;
					}
				}
				else
					emulator.emulate(instr);
			}
done:
			if (i != newarrIndex + 1)
				i--;
			newarrIndex = i;

			return resultValueArray;
		}

		static int findNewarr(MethodDefinition method, int arraySize) {
			for (int i = 0; ; i++) {
				int size;
				if (!findNewarr(method, ref i, out size))
					return -1;
				if (size == arraySize)
					return i;
			}
		}

		public static bool findNewarr(MethodDefinition method, ref int i, out int size) {
			var instructions = method.Body.Instructions;
			for (; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.Code != Code.Newarr || i < 1)
					continue;
				var ldci4 = instructions[i - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				size = DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			size = -1;
			return false;
		}
	}
}
