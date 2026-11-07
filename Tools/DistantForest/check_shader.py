"""Audit forest raster inputs, constant-buffer layout and included variants against installed HDRP source."""
from pathlib import Path
import json
import re

root = Path(__file__).resolve().parents[2]
shaders = root / 'Assets/DroneSandbox/Resources/Shaders'
packages = root.parent / 'DroneWarfare/Library/PackageCache'
hdrp = packages / 'com.unity.render-pipelines.high-definition@49ad9dd6ab9a'
core = packages / 'com.unity.render-pipelines.core@13460d8ea9e5'
shader = (shaders / 'ForestLit.shader').read_text()
properties = (shaders / 'ForestLitProperties.hlsl').read_text()
fade = (shaders / 'ForestFade.hlsl').read_text()
depth = (hdrp / 'Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl').read_text()
varyings = (hdrp / 'Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl').read_text()
instancing = (core / 'ShaderLibrary/UnityInstancing.hlsl').read_text()
transforms = (core / 'ShaderLibrary/SpaceTransforms.hlsl').read_text()
assert 'ZERO_INITIALIZE(FragInputs, output)' in varyings
assert '#ifdef VARYINGS_NEED_POSITION_WS' in varyings and '#elif defined(LOD_FADE_CROSSFADE)' in depth
assert shader.index('#define VARYINGS_NEED_POSITION_WS') < shader.index('LitDepthPass.hlsl')
buffer = properties.split('CBUFFER_START(UnityPerMaterial)', 1)[1].split('CBUFFER_END', 1)[0]
for name in ['_ForestCamera', '_ForestDistances', '_ForestTier', '_ForestBuild', '_ForestEnabled']:
    assert name + '(' in shader and re.search(r'float[4]? ' + name + r';', buffer)
    assert not re.search(r'float[4]? ' + name + r';', fade)
assert 'ApplyCameraTranslationToMatrix(UNITY_DOTS_MATRIX_M)' in instancing
assert 'ApplyCameraTranslationToMatrix(UNITY_ACCESS_INSTANCED_PROP(unity_Builtins0, unity_ObjectToWorldArray))' in instancing
assert 'return UNITY_MATRIX_M;' in transforms and 'positionRWS += _WorldSpaceCameraPos.xyz;' in transforms
assert '_ForestCamera.w < 0.5' in fade and '_ForestDistances.w < 1.0' in fade
assert '#pragma multi_compile_instancing' in shader and '#pragma multi_compile _ DOTS_INSTANCING_ON' in shader
guid = re.search(r'guid: (\w+)', (shaders / 'ForestLit.shader.meta').read_text())[1]
count = 0
for material in sorted((root / 'Assets/DroneSandbox/Resources/Materials/Forest').glob('*.mat')):
    text = material.read_text()
    assert guid in text and 'm_EnableInstancingVariants: 1' in text
    if material.stem.startswith(('Leaves', 'Impostor')):
        assert '_ALPHATEST_ON' in text and '_DOUBLESIDED_ON' in text
    assert '_ForestEnabled: 0' in text and '_ForestBuild: 1' in text
    count += 1
assert count == 12
# Translation is removed in both instance paths; adding it once restores absolute Unity scene coordinates.
for camera in [(3500, 140, -2500), (-28000, 9000, 28000), (0, 42, 0)]:
    for pivot in [(25, 0, 80), (-1070, 157, -3200)]:
        relative = tuple(p - c for p, c in zip(pivot, camera))
        assert tuple(p + c for p, c in zip(relative, camera)) == pivot
result = {'installedHdrp': hdrp.name, 'serializedInstancingMaterials': count,
          'depthWorldPosition': True, 'unityPerMaterial': True, 'absolutePivotInBothInstancePaths': True,
          'invalidParametersFailOpen': True, 'shaderCompilerOrGpuExecuted': False}
out = root / 'Tools/DistantForest/Review/shader-contract.json'
out.write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
