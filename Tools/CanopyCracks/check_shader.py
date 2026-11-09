"""Compile ForestFade/ForestDetail with Windows' D3DCompile, as Tools/CanopyRelief/check_shader.py does, for the new
per-pixel refinement signature. HDRP pass/variant compilation remains Claude's Unity build check."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / 'Tools/CanopyRelief/check_shader.py').read_text()
source = source.replace("ForestFade(p.xyz,uv);", "ForestFade(p.xyz,uv,(uint2)p.xy);")
assert "ForestFade(p.xyz,uv,(uint2)p.xy);" in source
exec(compile(source, str(ROOT / 'Tools/CanopyRelief/check_shader.py'), 'exec'))
