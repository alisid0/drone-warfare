"""Compile the forest and terrain integration against installed Unity assemblies, without starting Unity."""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
script = root / 'Tools/DistantForest/prepare.py'
source = script.read_text().replace("temp = root / 'Temp/distant-forest'", "temp = root / 'Temp/forest-above'")
source = source.replace("refs = ''.join", "files += [scripts / (n + '.cs') for n in ['VillagePlan', 'VillageLayout', 'VillageScenery', 'WorldLandmarkLayout.Yards', 'LandmarkClutter']]\nrefs = ''.join")
exec(compile(source, str(script), 'exec'))
