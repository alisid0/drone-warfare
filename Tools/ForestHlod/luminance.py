from pathlib import Path
import numpy as np,json
from PIL import Image
root=Path(__file__).resolve().parents[2];out=root/'Tools/ForestHlod/Review'
def value(name):
    a=np.array(Image.open(out/(name+'.png')).convert('RGB'),dtype=float)[300:600,600:1000]/255
    a=np.where(a<=.04045,a/12.92,((a+.055)/1.055)**2.4)
    return float((a@np.array([.2126,.7152,.0722])).mean())
a=value('calibration_cards');b=value('calibration_canopy')
result=dict(cards_luminance=a,canopy_luminance=b,relative_difference=b/a-1,roi=[600,300,1000,600],camera_distance=600,pitch_degrees=60)
(out/'luminance.json').write_text(json.dumps(result,indent=2));print(result)
assert abs(result["relative_difference"]) <= .10, "Handover luminance differs by more than ten percent"
