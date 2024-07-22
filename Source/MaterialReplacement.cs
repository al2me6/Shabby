﻿/*
This file is part of Shabby.

Shabby is free software: you can redistribute it and/or
modify it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Shabby is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Shabby.  If not, see
<http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shabby
{

class MaterialReplacement
{
	[Persistent(name = nameof(materialDef))] private string defName;
	public MaterialDef materialDef = null;

	public List<string> targetMaterials;
	public List<string> targetTransforms;
	public bool blanketApply = false;

	readonly Dictionary<Material, Material> replacedMaterials = new Dictionary<Material, Material>();

	public MaterialReplacement(ConfigNode node)
	{
		ConfigNode.LoadObjectFromConfig(this, node);

		if (string.IsNullOrEmpty(defName)) {
			Debug.LogError("[Shabby]: material replacement must reference a material definition");
		} else if (!MaterialDefLibrary.items.TryGetValue(defName, out materialDef)) {
			Debug.LogError($"[Shabby]: failed to find material definition {defName}");
		}

		targetMaterials = node.GetValuesList("targetMaterial");
		targetTransforms = node.GetValuesList("targetTransform");

		if (targetMaterials.Count > 0 && targetTransforms.Count > 0) {
			Debug.LogError($"[Shabby]: material replacement {defName} may not specify both materials and transforms");
			targetTransforms.Clear();
		}

		if (targetMaterials.Count == 0 && targetTransforms.Count == 0) {
			blanketApply = true;
		}
	}

	public bool MatchMaterial(Renderer renderer) => targetMaterials.Contains(renderer.sharedMaterial.name);

	public bool MatchTransform(Transform transform) => targetTransforms.Contains(transform.name);

	public void ApplyToSharedMaterial(Renderer renderer)
	{
		var sharedMat = renderer.sharedMaterial;
		if (!replacedMaterials.TryGetValue(sharedMat, out var replacementMat)) {
			replacementMat = new Material(sharedMat);
			materialDef.ApplyTo(replacementMat);
			replacedMaterials[sharedMat] = replacementMat;
		}
		renderer.sharedMaterial = replacementMat;
	}
}

[HarmonyPatch(typeof(PartLoader), "CompileModel")]
public class MaterialReplacementPatch
{
	static void Postfix(ref GameObject __result, ConfigNode partCfg)
	{
		const string replacementNodeName = "SHABBY_MATERIAL_REPLACE";
		if (!partCfg.HasNode(replacementNodeName)) return;

		var replacements = new List<MaterialReplacement>();
		foreach (ConfigNode node in partCfg.nodes) {
			if (node.name != replacementNodeName) continue;
			var replacement = new MaterialReplacement(node);
			if (replacement.materialDef != null) replacements.Add(replacement);
		}

		// Apply blanket replacements and material name replacements.
		foreach (var renderer in __result.GetComponentsInChildren<Renderer>()) {
			foreach (var replacement in replacements) {
				if (!replacement.blanketApply && !replacement.MatchMaterial(renderer)) continue;
				replacement.ApplyToSharedMaterial(renderer);
				break;
			}
		}

		// Apply transform replacements if any.
		if (replacements.Any(rep => rep.targetTransforms.Count > 0)) {
			foreach (var transform in __result.GetComponentsInChildren<Transform>()) {
				foreach (var replacement in replacements) {
					if (!replacement.MatchTransform(transform)) continue;
					foreach (var renderer in transform.GetComponentsInChildren<Renderer>()) {
						replacement.ApplyToSharedMaterial(renderer);
					}
					break;
				}
			}
		}

		var replacementNames = string.Join(", ", replacements.Select(rep => rep.materialDef.name));
		Debug.Log($"[Shabby] applied material replacements {replacementNames}");
	}
}

}
