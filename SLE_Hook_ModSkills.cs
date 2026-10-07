using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SkillLimitExtender
{
    /// <summary>
    /// 汎用MODスキルパッチシステム - すべてのMODスキルの100f制限を自動検出・パッチ
    /// </summary>
    internal static class SLE_Hook_ModSkills
    {
        private static Harmony? _harmony;
        private static readonly List<string> _patchedAssemblies = new List<string>();

        internal static void Initialize(Harmony harmony)
        {
            _harmony = harmony;
            SkillLimitExtenderPlugin.Logger?.LogInfo("[SLE] ModSkills: Starting universal MOD skill patch system...");

            try
            {
                // すべてのロードされたアセンブリを検索
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                int totalPatched = 0;

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        // MODアセンブリかどうかチェック（BepInExプラグインまたはスキル関連）
                        if (IsModSkillAssembly(assembly))
                        {
                            int patchedInAssembly = PatchModSkillAssembly(assembly);
                            if (patchedInAssembly > 0)
                            {
                                totalPatched += patchedInAssembly;
                                _patchedAssemblies.Add(assembly.GetName().Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (SkillLimitExtenderPlugin.EnableGrowthCurveDebug?.Value == true)
                        {
                            SkillLimitExtenderPlugin.Logger?.LogDebug($"[SLE] ModSkills: Skipping assembly {assembly.GetName().Name}: {ex.Message}");
                        }
                        continue;
                    }
                }

                SkillLimitExtenderPlugin.Logger?.LogInfo($"[SLE] ModSkills: Successfully patched {totalPatched} methods across {_patchedAssemblies.Count} MOD assemblies");
                if (_patchedAssemblies.Count > 0)
                {
                    SkillLimitExtenderPlugin.Logger?.LogInfo($"[SLE] ModSkills: Patched assemblies: {string.Join(", ", _patchedAssemblies)}");
                }
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] ModSkills: Initialization failed: {ex}");
            }
        }

        /// <summary>
        /// アセンブリがMODスキル関連かどうかを判定
        /// </summary>
        private static bool IsModSkillAssembly(Assembly assembly)
        {
            try
            {
                var name = assembly.GetName().Name;
                
                // 除外するアセンブリ（バニラやフレームワーク）
                if (name.StartsWith("Assembly-CSharp") ||
                    name.StartsWith("UnityEngine") ||
                    name.StartsWith("System") ||
                    name.StartsWith("mscorlib") ||
                    name.StartsWith("BepInEx") ||
                    name.StartsWith("0Harmony") ||
                    name.StartsWith("Jotunn") ||
                    name == "SkillLimitExtender")
                {
                    return false;
                }

                // MODスキル関連のキーワードを含むかチェック
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    // HarmonyPatchアトリビュートを持つクラスを探す
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    {
                        // スキル関連のメソッドをパッチしているかチェック
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var method in methods)
                        {
                            if (method.Name.Contains("Skill") || 
                                method.Name.Contains("Raise") ||
                                method.Name.Contains("Cheat"))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// MODアセンブリ内の100f制限メソッドをパッチ
        /// </summary>
        private static int PatchModSkillAssembly(Assembly assembly)
        {
            int patchedCount = 0;
            
            try
            {
                var types = assembly.GetTypes();
                
                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    
                    foreach (var method in methods)
                    {
                        // スキル関連メソッドで100f制限を含むものを検索
                        if (IsSkillLimitMethod(method))
                        {
                            try
                            {
                                // Transpilerパッチを適用
                                var transpiler = new HarmonyMethod(typeof(SLE_Hook_ModSkills), nameof(UniversalSkillLimitTranspiler));
                                _harmony?.Patch(method, transpiler: transpiler);
                                
                                SkillLimitExtenderPlugin.Logger?.LogInfo($"[SLE] ModSkills: Patched {assembly.GetName().Name}.{type.Name}.{method.Name}");
                                patchedCount++;
                            }
                            catch (Exception ex)
                            {
                                SkillLimitExtenderPlugin.Logger?.LogWarning($"[SLE] ModSkills: Failed to patch {type.Name}.{method.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] ModSkills: Error processing assembly {assembly.GetName().Name}: {ex}");
            }

            return patchedCount;
        }

        /// <summary>
        /// メソッドがスキル制限関連かどうかを判定
        /// </summary>
        private static bool IsSkillLimitMethod(MethodInfo method)
        {
            try
            {
                // メソッド名でフィルタリング
                if (!method.Name.Contains("Skill") && 
                    !method.Name.Contains("Raise") && 
                    !method.Name.Contains("Cheat") &&
                    !method.Name.Contains("Level"))
                {
                    return false;
                }

                // ILコードを調べて100f定数を含むかチェック
                var instructions = HarmonyLib.PatchProcessor.GetCurrentInstructions(method);
                bool contains100f = instructions.Any(instr => 
                    instr.opcode == OpCodes.Ldc_R4 && 
                    instr.operand is float f && 
                    Math.Abs(f - 100f) < 0.001f);

                if (contains100f)
                {
                    // Clamp呼び出しの文脈で100fが使われているかチェック
                    bool hasClampContext = instructions.Any(instr =>
                        instr.opcode == OpCodes.Call &&
                        instr.operand?.ToString()?.Contains("Clamp") == true);

                    return hasClampContext;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 汎用スキル制限Transpiler - すべてのMODスキルの100f制限を動的上限に置き換え
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UniversalSkillLimitTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = new List<CodeInstruction>(instructions);

            try
            {
                // 100f定数を動的上限に置き換え
                for (int i = 0; i < codes.Count; i++)
                {
                    var instr = codes[i];

                    if (instr.opcode == OpCodes.Ldc_R4 && 
                        instr.operand is float f && 
                        Math.Abs(f - 100f) < 0.001f)
                    {
                        // Clamp文脈かチェック
                        bool isClampContext = false;
                        for (int j = Math.Max(0, i - 5); j < Math.Min(codes.Count, i + 5); j++)
                        {
                            if (codes[j].opcode == OpCodes.Call &&
                                codes[j].operand?.ToString()?.Contains("Clamp") == true)
                            {
                                isClampContext = true;
                                break;
                            }
                        }

                        if (isClampContext)
                        {
                            // 100fを汎用的な上限取得メソッドに置き換え
                            var newSeq = new List<CodeInstruction>
                            {
                                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SLE_Hook_ModSkills), nameof(GetUniversalSkillCap))),
                                new CodeInstruction(OpCodes.Conv_R4)
                            };

                            codes[i] = newSeq[0];
                            codes.InsertRange(i + 1, newSeq.GetRange(1, newSeq.Count - 1));
                            
                            SkillLimitExtenderPlugin.Logger?.LogInfo($"[SLE] ModSkills: Replaced 100f in {original.DeclaringType?.Name}.{original.Name}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SkillLimitExtenderPlugin.Logger?.LogError($"[SLE] ModSkills: Transpiler error in {original.DeclaringType?.Name}.{original.Name}: {ex}");
            }

            return codes;
        }

        /// <summary>
        /// 汎用スキル上限取得メソッド - 文脈に応じて適切な上限を返す
        /// </summary>
        private static int GetUniversalSkillCap()
        {
            try
            {
                // 現在のスタックトレースから呼び出し元を特定し、適切なスキル上限を返す
                var stackTrace = new System.Diagnostics.StackTrace();
                
                // CartographySkillの場合
                for (int i = 0; i < stackTrace.FrameCount; i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method?.DeclaringType?.FullName?.Contains("CartographySkill") == true)
                    {
                        return SkillConfigManager.GetCartographySkillCap();
                    }
                }

                // その他のMODスキルの場合はデフォルト上限
                return SkillConfigManager.DefaultCapFallback;
            }
            catch
            {
                return SkillConfigManager.DefaultCapFallback; // Fallback to the configured fork default
            }
        }
    }
}