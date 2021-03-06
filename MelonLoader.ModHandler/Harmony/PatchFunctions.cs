using Harmony.ILCopying;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MelonLoader;

namespace Harmony
{
	public static class PatchFunctions
	{
		public static void AddPrefix(PatchInfo patchInfo, string owner, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddPrefix(info.method, owner, priority, before, after);
		}

		public static void RemovePrefix(PatchInfo patchInfo, string owner)
		{
			patchInfo.RemovePrefix(owner);
		}

		public static void AddPostfix(PatchInfo patchInfo, string owner, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddPostfix(info.method, owner, priority, before, after);
		}

		public static void RemovePostfix(PatchInfo patchInfo, string owner)
		{
			patchInfo.RemovePostfix(owner);
		}

		public static void AddTranspiler(PatchInfo patchInfo, string owner, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddTranspiler(info.method, owner, priority, before, after);
		}

		public static void RemoveTranspiler(PatchInfo patchInfo, string owner)
		{
			patchInfo.RemoveTranspiler(owner);
		}

		public static void RemovePatch(PatchInfo patchInfo, MethodInfo patch)
		{
			patchInfo.RemovePatch(patch);
		}

		// pass in a generator that will create local variables for the returned instructions
		//
		public static List<ILInstruction> GetInstructions(ILGenerator generator, MethodBase method)
		{
			return MethodBodyReader.GetInstructions(generator, method);
		}

		public static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches)
		{
			return patches
				.Where(p => p.patch != null)
				.OrderBy(p => p)
				.Select(p => p.GetMethod(original))
				.ToList();
		}

		public static DynamicMethod UpdateWrapper(MethodBase original, PatchInfo patchInfo, string instanceID)
		{
			var sortedPrefixes = GetSortedPatchMethods(original, patchInfo.prefixes);
			var sortedPostfixes = GetSortedPatchMethods(original, patchInfo.postfixes);
			var sortedTranspilers = GetSortedPatchMethods(original, patchInfo.transpilers);

			var replacement = MethodPatcher.CreatePatchedMethod(original, instanceID, sortedPrefixes, sortedPostfixes, sortedTranspilers);
			if (replacement == null) throw new MissingMethodException("Cannot create dynamic replacement for " + original.FullDescription());

			var errorString = Memory.DetourMethod(original, replacement);
			if (errorString != null)
				throw new FormatException("Method " + original.FullDescription() + " cannot be patched. Reason: " + errorString);

			if (UnhollowerSupport.IsGeneratedAssemblyType(original.DeclaringType))
			{
				var il2CppShim = CreateIl2CppShim(replacement, original);
				var origMethodPtr = UnhollowerSupport.MethodBaseToIntPtr(original);
				var oldDetourPtr = patchInfo.methodDetourPointer;
				var newDetourPtr = il2CppShim.MethodHandle.GetFunctionPointer();

				if (oldDetourPtr != IntPtr.Zero)
					Imports.Unhook(origMethodPtr, oldDetourPtr);
				Imports.Hook(origMethodPtr, newDetourPtr);
				patchInfo.methodDetourPointer = newDetourPtr;

				PatchTools.RememberObject(original, new PotatoTuple { First = replacement, Second = il2CppShim});
			}
			else
				PatchTools.RememberObject(original, replacement); // no gc for new value + release old value to gc
			return replacement;
		}

		private class PotatoTuple
		{
			public MethodBase First;
			public MethodBase Second;
		}

		private static DynamicMethod CreateIl2CppShim(DynamicMethod patch, MethodBase original)
		{
			var patchName = patch.Name + "_il2cpp";

			var parameters = patch.GetParameters();
			var result = parameters.Types().ToList();
			var origParamTypes = result.ToArray();
			var paramTypes = new Type[origParamTypes.Length];
			for (int i = 0; i < paramTypes.Length; ++i)
				paramTypes[i] = Il2CppTypeForPatchType(origParamTypes[i]);

			var origReturnType = AccessTools.GetReturnedType(patch);
			var returnType = Il2CppTypeForPatchType(origReturnType);

			DynamicMethod method = new DynamicMethod(
					patchName,
					MethodAttributes.Public | MethodAttributes.Static,
					CallingConventions.Standard,
					returnType,
					paramTypes,
					original.DeclaringType,
					true
			);

			for (var i = 0; i < parameters.Length; i++)
				method.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);

			var il = method.GetILGenerator();

			LocalBuilder[] byRefValues = new LocalBuilder[parameters.Length];
			LocalBuilder returnLocal = null;
			if (origReturnType != typeof(void)) {
				returnLocal = il.DeclareLocal(origReturnType);
				Emitter.LogLocalVariable(il, returnLocal);
			}
			LocalBuilder exceptionLocal = il.DeclareLocal(typeof(Exception));
			Emitter.LogLocalVariable(il, exceptionLocal);

			// Start a try-block for the call to the original patch
			Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock, null), out _);

			// Load arguments, invoking the IntPrt -> Il2CppObject constructor for IL2CPP types
			for (int i = 0; i < origParamTypes.Length; ++i) {
				Emitter.Emit(il, OpCodes.Ldarg, i);
				ConvertArgument(il, origParamTypes[i], ref byRefValues[i]);
				if (byRefValues[i] != null) {
					Emitter.LogLocalVariable(il, byRefValues[i]);
				}
			}

			// Call the original patch with the now-correct types
			Emitter.Emit(il, OpCodes.Call, patch);

			// Store the result, if any
			if (returnLocal != null) {
				Emitter.Emit(il, OpCodes.Stloc, returnLocal);
			}

			// Catch any exceptions that may have been thrown
			Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)), out _);

			// MelonModLogger.LogError("Exception in ...\n" + exception.ToString());
			Emitter.Emit(il, OpCodes.Stloc, exceptionLocal);
			Emitter.Emit(il, OpCodes.Ldstr, $"Exception in Harmony patch of method {original.FullDescription()}:\n");
			Emitter.Emit(il, OpCodes.Ldloc, exceptionLocal);
			Emitter.Emit(il, OpCodes.Call, AccessTools.DeclaredMethod(typeof(Exception), "ToString", new Type[0]));
			Emitter.Emit(il, OpCodes.Call, AccessTools.DeclaredMethod(typeof(string), "Concat", new Type[] { typeof(string), typeof(string) }));
			Emitter.Emit(il, OpCodes.Call, AccessTools.DeclaredMethod(typeof(MelonModLogger), "LogError", new Type[] { typeof(string) }));

			// Close the exception block
			Emitter.MarkBlockAfter(il, new ExceptionBlock(ExceptionBlockType.EndExceptionBlock, null));

			// Write back the pointers of ref arguments
			for (int i = 0; i < parameters.Length; ++i) {
				if (byRefValues[i] == null) continue;

				Emitter.Emit(il, OpCodes.Ldarg, i); // -> [intptr*]
				Emitter.Emit(il, OpCodes.Ldloc, byRefValues[i]); // -> [intptr*, obj]
				if (origParamTypes[i].GetElementType() == typeof(string)) {
					Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.ManagedStringToIl2CppMethod); // -> [intptr*, intptr]
				} else {
					Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.Il2CppObjectBaseToPtrMethod); // -> [intptr*, intptr]
				}
				Emitter.Emit(il, OpCodes.Stind_I); // -> []
			}

			// Load the return value, if any, and unwrap it if required
			if (returnLocal != null) {
				Emitter.Emit(il, OpCodes.Ldloc, returnLocal);
				ConvertReturnValue(il, origReturnType);
			}

			Emitter.Emit(il, OpCodes.Ret);

			DynamicTools.PrepareDynamicMethod(method);
			return method;
		}

		private static Type Il2CppTypeForPatchType(Type type) {
			if (type.IsByRef) {
				Type element = type.GetElementType();
				if (element == typeof(string) || UnhollowerSupport.IsGeneratedAssemblyType(element)) {
					return typeof(IntPtr*);
				} else {
					return type;
				}
			} else if (type == typeof(string) || UnhollowerSupport.IsGeneratedAssemblyType(type)) {
				return typeof(IntPtr);
			} else {
				return type;
			}
		}

		private static void ConvertArgument(ILGenerator il, Type paramType, ref LocalBuilder byRefLocal) {
			if (paramType.IsValueType)
				return;

			if (paramType.IsByRef) {
				Type elementType = paramType.GetElementType();

				if (paramType.GetElementType() == typeof(string)) {
					// byRefLocal = Il2CppStringToManaged(*ptr);
					// return ref byRefLocal;

					byRefLocal = il.DeclareLocal(elementType);
					Emitter.Emit(il, OpCodes.Ldind_I);
					Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.Il2CppStringToManagedMethod);
					Emitter.Emit(il, OpCodes.Stloc, byRefLocal);
					Emitter.Emit(il, OpCodes.Ldloca, byRefLocal);
				} else if (UnhollowerSupport.IsGeneratedAssemblyType(elementType)) {
					// byRefLocal = *ptr == 0 ? null : new SomeType(*ptr);
					// return ref byRefLocal;
					Label ptrNonZero = il.DefineLabel();
					Label done = il.DefineLabel();

					byRefLocal = il.DeclareLocal(elementType);
					Emitter.Emit(il, OpCodes.Ldind_I);
					Emitter.Emit(il, OpCodes.Dup);
					Emitter.Emit(il, OpCodes.Brtrue_S, ptrNonZero);
					Emitter.Emit(il, OpCodes.Pop);
					Emitter.Emit(il, OpCodes.Br_S, done);
					Emitter.MarkLabel(il, ptrNonZero);
					Emitter.Emit(il, OpCodes.Newobj, Il2CppConstuctor(elementType));
					Emitter.Emit(il, OpCodes.Stloc, byRefLocal);
					Emitter.MarkLabel(il, done);
					Emitter.Emit(il, OpCodes.Ldloca, byRefLocal);
				}
			} else if (paramType == typeof(string)) {
				// return Il2CppStringToManaged(ptr);
				Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.Il2CppStringToManagedMethod);
			} else if (UnhollowerSupport.IsGeneratedAssemblyType(paramType)) {
				// return ptr == 0 ? null : new SomeType(ptr);
				Label ptrNonZero = il.DefineLabel();
				Label done = il.DefineLabel();

				Emitter.Emit(il, OpCodes.Dup);
				Emitter.Emit(il, OpCodes.Brtrue_S, ptrNonZero);
				Emitter.Emit(il, OpCodes.Pop);
				Emitter.Emit(il, OpCodes.Ldnull);
				Emitter.Emit(il, OpCodes.Br_S, done);
				Emitter.MarkLabel(il, ptrNonZero);
				Emitter.Emit(il, OpCodes.Newobj, Il2CppConstuctor(paramType));
				Emitter.MarkLabel(il, done);
			}
		}

		private static void ConvertReturnValue(ILGenerator il, Type returnType) {
			if (returnType == typeof(string)) {
				Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.ManagedStringToIl2CppMethod);
			} else if (!returnType.IsValueType && UnhollowerSupport.IsGeneratedAssemblyType(returnType)) {
				Emitter.Emit(il, OpCodes.Call, UnhollowerSupport.Il2CppObjectBaseToPtrMethod);
			}
		}

		private static ConstructorInfo Il2CppConstuctor(Type type) => AccessTools.DeclaredConstructor(type, new Type[] { typeof(IntPtr) });
	}
}
