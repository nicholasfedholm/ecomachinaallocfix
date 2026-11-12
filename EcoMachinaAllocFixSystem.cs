using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EcoMachinaAllocFix
{
    /// <summary>
    /// Harmony prefixes on Eco Machina's private mesh-append helpers (RenderOnlyTaperedWoodPatches).
    /// Root cause: EnsureTargetAppendCapacity resized the pool target buffers to the exact required length on every
    /// insertion (quadratic copying, one dead LOH Int32[] per insert), and TryAlignCustom* gave every source mesh an
    /// EmptyClone() sized to the whole target pool part instead of the source vertex count.
    /// </summary>
    public class EcoMachinaAllocFixSystem : ModSystem
    {
        public const string HarmonyId = "ecomachinaallocfix";
        const string TargetTypeName = "vsecomachina.Integration.Rendering.RenderOnlyTaperedWoodPatches";

        Harmony harmony;
        static System.Func<int, int[], int, int> resolveFn;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            Type t = AccessTools.TypeByName(TargetTypeName);
            if (t == null)
            {
                api.Logger.Warning("[EcoMachinaAllocFix] Eco Machina type {0} not found; nothing patched.", TargetTypeName);
                return;
            }

            MethodInfo resolveElementsPerVertex = AccessTools.Method(t, "ResolveElementsPerVertex");
            if (resolveElementsPerVertex == null)
            {
                api.Logger.Warning("[EcoMachinaAllocFix] ResolveElementsPerVertex not found; nothing patched (Eco Machina version mismatch?).");
                return;
            }
            resolveFn = (System.Func<int, int[], int, int>)Delegate.CreateDelegate(typeof(System.Func<int, int[], int, int>), resolveElementsPerVertex);

            harmony = new Harmony(HarmonyId);
            int ok = 0;
            ok += Patch(api, t, "EnsureTargetAppendCapacity", nameof(EnsureCapacityPrefix));
            ok += Patch(api, t, "TryAlignCustomInts", nameof(PreAlignInts));
            ok += Patch(api, t, "TryAlignCustomFloats", nameof(PreAlignFloats));
            ok += Patch(api, t, "TryAlignCustomShorts", nameof(PreAlignShorts));
            ok += Patch(api, t, "TryAlignCustomBytes", nameof(PreAlignBytes));
            api.Logger.Notification("[EcoMachinaAllocFix] patched {0}/5 Eco Machina mesh-append helpers.", ok);
        }

        int Patch(ICoreClientAPI api, Type t, string target, string prefixName)
        {
            MethodInfo original = AccessTools.Method(t, target);
            MethodInfo prefix = AccessTools.Method(typeof(EcoMachinaAllocFixSystem), prefixName);
            if (original == null || prefix == null)
            {
                api.Logger.Warning("[EcoMachinaAllocFix] could not resolve {0}; skipped.", target);
                return 0;
            }
            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                return 1;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[EcoMachinaAllocFix] patching {0} failed: {1}", target, ex.Message);
                return 0;
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }

        // Replacement for RenderOnlyTaperedWoodPatches.EnsureTargetAppendCapacity: geometric growth instead of exact-fit.
        static bool EnsureCapacityPrefix(MeshData target, MeshData source)
        {
            int minVertices = Math.Max(4, target.VerticesCount + source.VerticesCount);
            int minIndices = Math.Max(6, target.IndicesCount + source.IndicesCount);

            if (target.VerticesMax < minVertices)
            {
                target.VerticesMax = Math.Max(minVertices, target.VerticesMax + (target.VerticesMax >> 1));
            }
            if (target.IndicesMax < minIndices)
            {
                target.IndicesMax = Math.Max(minIndices, target.IndicesMax + (target.IndicesMax >> 1));
            }

            int vmax = target.VerticesMax;
            Grow(ref target.xyz, vmax * 3);
            if (target.Uv != null) Grow(ref target.Uv, vmax * 2);
            if (target.Rgba != null) Grow(ref target.Rgba, vmax * 4);
            if (target.Flags != null) Grow(ref target.Flags, vmax);
            if (target.Normals != null) Grow(ref target.Normals, vmax);
            Grow(ref target.Indices, target.IndicesMax);
            return false; // skip original
        }

        static void Grow<T>(ref T[] buffer, int requiredLength)
        {
            int len = buffer == null ? 0 : buffer.Length;
            if (requiredLength <= len || requiredLength <= 0) return;
            Array.Resize(ref buffer, requiredLength);
        }

        // Pre-seed the source custom part with a placeholder sized to the source (original used targetPart.EmptyClone(),
        // which allocates a buffer as long as the whole pool part).
        static void PreAlignInts(MeshData target, MeshData source)
        {
            CustomMeshDataPartInt tp = target.CustomInts;
            if (tp == null || source.CustomInts != null) return;
            int required = source.VerticesCount * resolveFn(tp.InterleaveStride, tp.InterleaveSizes, 4);
            if (required <= 0) return;
            var sp = new CustomMeshDataPartInt(required);
            CopyLayout(tp, sp);
            source.CustomInts = sp;
        }

        static void PreAlignFloats(MeshData target, MeshData source)
        {
            CustomMeshDataPartFloat tp = target.CustomFloats;
            if (tp == null || source.CustomFloats != null) return;
            int required = source.VerticesCount * resolveFn(tp.InterleaveStride, tp.InterleaveSizes, 4);
            if (required <= 0) return;
            var sp = new CustomMeshDataPartFloat(required);
            CopyLayout(tp, sp);
            source.CustomFloats = sp;
        }

        static void PreAlignShorts(MeshData target, MeshData source)
        {
            CustomMeshDataPartShort tp = target.CustomShorts;
            if (tp == null || source.CustomShorts != null) return;
            int required = source.VerticesCount * resolveFn(tp.InterleaveStride, tp.InterleaveSizes, 2);
            if (required <= 0) return;
            var sp = new CustomMeshDataPartShort(required);
            CopyLayout(tp, sp);
            source.CustomShorts = sp;
        }

        static void PreAlignBytes(MeshData target, MeshData source)
        {
            CustomMeshDataPartByte tp = target.CustomBytes;
            if (tp == null || source.CustomBytes != null) return;
            int required = source.VerticesCount * resolveFn(tp.InterleaveStride, tp.InterleaveSizes, 1);
            if (required <= 0) return;
            var sp = new CustomMeshDataPartByte(required);
            CopyLayout(tp, sp);
            source.CustomBytes = sp;
        }

        // Mirrors CustomMeshDataPart<T>.EmptyClone minus the full-capacity value buffer.
        static void CopyLayout<T>(CustomMeshDataPart<T> from, CustomMeshDataPart<T> to)
        {
            to.Count = 0;
            if (from.InterleaveSizes != null) to.InterleaveSizes = (int[])from.InterleaveSizes.Clone();
            if (from.InterleaveOffsets != null) to.InterleaveOffsets = (int[])from.InterleaveOffsets.Clone();
            to.InterleaveStride = from.InterleaveStride;
            to.Instanced = from.Instanced;
            to.StaticDraw = from.StaticDraw;
            to.BaseOffset = from.BaseOffset;
            // EmptyClone also copies the private customAllocationSize/allocationSize pair (they drive AllocationSize).
            // The fields must be resolved per closed generic type: a FieldInfo from CustomMeshDataPart<int> throws
            // ArgumentException when applied to a CustomMeshDataPart<float/short/byte>.
            var f = AllocFields<T>.Flag;
            if (f != null) f.SetValue(to, f.GetValue(from));
            var a = AllocFields<T>.Size;
            if (a != null) a.SetValue(to, a.GetValue(from));
        }

        static class AllocFields<T>
        {
            public static readonly FieldInfo Flag = AccessTools.Field(typeof(CustomMeshDataPart<T>), "customAllocationSize");
            public static readonly FieldInfo Size = AccessTools.Field(typeof(CustomMeshDataPart<T>), "allocationSize");
        }
    }
}
