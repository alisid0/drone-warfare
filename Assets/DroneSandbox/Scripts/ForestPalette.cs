using UnityEngine;

namespace DroneSandbox
{
    // Calibrated sRGB crown colours; the photo models and canopy share the same species correction.
    public static class ForestPalette
    {
        public static readonly Color32[] Colours =
        {
            new Color32(84, 108, 70, 255),
            new Color32(89, 112, 73, 255),
            new Color32(62, 88, 62, 255),
            new Color32(66, 92, 63, 255),
        };
        public static readonly float[] RadiusPerHeight = { 0.39738891f, 0.17586441f, 0.21163066f, 0.21589965f };
        public static readonly Color Ground = new Color(49f / 255f, 70f / 255f, 45f / 255f).linear;
        static readonly Color32[] PhotoColours = { new Color32(77, 98, 51, 255), new Color32(99, 116, 61, 255),
            new Color32(64, 87, 58, 255), new Color32(80, 99, 64, 255) };
        static readonly byte[] Pictures = System.Convert.FromBase64String(
            "LQAtAC0AsQB1DnlehQCKNXcpgTJ5XC0ALQCxAC0ALQAtALEAYAAtAIFRff9x/37of0N//3rofXN8cS0ALQAtAEwALQCGLX+Ucf+Eu2Vaa/98/3b/dP99/3XXVQBpAC0A" +
            "LQBwG2sVf6Fs8GynfFdw/3tBVAhytIL/gft2/3E1LQBpAG7/dP9zMnUfe/6Ar2xxc0h+vnrMbE6Qcoa+ht2cAHAccKdyz3Pkd6CBf2ZrUyOFqpf/f/94RoX/kOaS1qgA" +
            "b2GD/3PBe3iE/3hSUwBiVHJ7dv95/3zjf+eC/4LxLQBtaXb/ef9t5GrFjyZqsWX/Y69qaGZNc/iO/4b/fv+mUnplasJ2rGlBQxx+rmz/Zv9s52pael+T/4v/f/+B/5hY" +
            "dIx3/HS1ffyH3ofGdblnZmh2hd2C/5P/gPuH/5r/k+N8D385d/+C/4b3ha5eRi0AayV9042KhKGM/5OulaS7IS0AhAt794b/i8+D/5B2Vj9964bdmmFyl4v8ka+FjZ4A" +
            "mwCKg4L/g/eFtIj8juqE/4vWgax/eXlpfzCHg4GQ5gAtAC0AhliF/4T/f7KX55vDndeI7qmMgoCMuC0ALQCxAC0ALQAtAIYzkAOHS5P/nvib/6P/mMuJv4eTsQCLAC0A" +
            "LQAtALEALQAtAJYAjKKgwrxepi9+Pb8ATAAtAC0ALQAtAOYATACvAKQsjP+b0oNigO53AJQZhRvfAC0ALQAtAC0ALQAtAJcrjOR//4f/mnOXv34jhu6F/+YA3wAtAC0A" +
            "TACtDpeKgv95/3n/heqN/4D/fqF7NaRzkaptAC0A5gB5AIfXjO5//4X/k/+F/4L4i3txfY1GmPSk/6o8LQCyAHFueOaVtY7/gPeE/5P/jf+BiqJxgtJ/r4v/p//lAC0A" +
            "Zh6B/pB4f6J7wnLccWl9woX3e+1+y5PGpOek/57B5gByuIT/fbZwKYzrbuRmFW51fLxvN3e5fv+Py57oiv+gAID4ef9+3X3rhv99/2mrbb5otEMALQByYZr/lMyLZoYA" +
            "eFCA/2ynbkGG/4nwYF9h/27/a5pXN4/NiPSH8X49LQB+/5b/dLttAHI8bSFiQ2j/af9v84uNdeqD5HzjiP+KHH0UeTl8+3rxdKpdcV4tSACAhH+UiveH3ZPcitSK+oUL" +
            "fQB+k3fxbNR4moGvclt9aXdbWkZxh4n/ev+F/4HujAB5AIkRc/Rkf3seeP99nn7Jgv9emHBOd/938H6ThqBwAC0ALQB1wn3/efdmvHEUcNx5aXj0ecxwoHmxLQCCAKkA" +
            "5gDmAHpbfr501nTWgTl1zHGlef9v+m3LeuOFAC0ALQAtAC0AZAAtALQAgcxxL3T/cb93/2//gHJtAGwApgAtAC0AswAtAC0AjQqIvIP4gP97B5IAeqptAC0ATAAtAC0A" +
            "LQAtAKIATACCgJL/g/x8/33FeLV29XibbAAtAKYALQCzAJkHkfWIjX3TfuCcoIP/e/9312mQa/Nz63IjLQDmAC0ApMmU/6H/isaG/3z1hdyF/3SdgLx8/4T/capWAC0A" +
            "1yOn/5/ciciV+pDwfNCL+3evPAhuzX/Lb696zG65TACUspH/pOSV/2WUbhJyVIXPgl9jkHT/eXZ5vHn/crVsAJv/hP+BhIXmcpswAGaDa/R3zYQwgNl14XCxfSt6DowA" +
            "pHmokILPetR+nkAAbYl4/2jThgB8hn+DcwN6g4Dzjyt+k24fh3uNzXuleL5o5mj/Z+FhDmw5fPNt8Gv/dl4tAIE5er57Uo1YgP98pGyJZa9aJmJIZ46GnXRifuF+1GYA" +
            "lVCF9YDhiP+K/2Epcz1quIrtbw5wk3LIYRl45nv/dkqD66KliP+F9Zn/daFpp33/iv97xnwtbv9s9Hb8elotAIpri5GK+oj/if95/3vghf90/3NNZQ12/4DohXUtAOYA" +
            "qjCU/4T/ef+U/33/f69wM4C/cXZw8H7wdgt7ZbMAswDUFZH/gsx+/3f/iPqJ/2Ycf/B5/3j/j/9+My0A5gAtAJwAhwCrgImqg/+I/JybjNOC/3n/doZ+in8vLQBMAC0A" +
            "LQAtAC0ALQBWAIClckRqfGv/b415PC0ALQB4AC0ALQB4AOYAdBJ2zG3hbP9l/23/cf9w/2yOggBkDi0ALQAtAC0ALQBsjoD/ftpr/2j/c/+D/3vad7F1xnf3lDlzAC0A" +
            "dwBSAGexZaEtAJJllv9v/27/b/93/5LekfOH0HgAeABsVXvwa/V24asAhQ+H/3H/dN17/2/cXjJv/2vSaQAtAGgbdv9q/4COimx27XPmiP+p/+TxcMOKtnP/bv946H4E" +
            "Y6tp/2n/a/9s/3P/cv+Q/JeAvfx50oL/eP9y/4v/kEtp/3T/dv9u/2//b/9q/3z/wf/m6nT/dd6D84G2clKLAGA5af94/3r/bs9x/3f/cf/Ku3z8b/9s/3Pxf4ktAC0A" +
            "ZDVr7nz4jv+ga3Ohfv9r/6sRkch1022qabxuFpIASwAtAGukg/+ugOaibv9rfXnUennmS3Tcgf9vxnYwh/98e0kAed6g/2BRg1By/2b/hP+c/+Z/5gB6RnTocv97bJYA" +
            "ZQB6r5duZgBtsYL/ef97/4fr5gCatJfZi9x97i0ALQAtAFwALQBYAHT/g/9z/336fadlFoH/0P+8/7FpeADmAC0ALQCWAEEAfaGV/3f/cP+UxYv/nv/Qy6OW5gAtAOYA" +
            "LQAtAOYALQCRAHEjgRF2iq1ywOG3w9wOLQAtAC0ALQAtAC0ALQAtAHIAswB+xoC1WgCFQcZKohQtAC0A5gAtAC0ALQAtAC0ALQAtAIk2eN1w+IT/m/+m/uYR1wAtAOYA" +
            "5gCJAIeRg9NgAHY2ZhFvKHT/eXai/7X/s/+wr64AeQAtAG0Adf+D/4PLgb9vzWnmfP9/GZTqhP9s/67/5i0tADQAbDlz/23/fP9942v/dZFx/5A25hmuAJNKoO2YTi0A" +
            "kbZ8/23/ev9+/3jeaf9s8Y3G5mLkqpSifdl33HH/oQB+45L4Zmx7fY6gZ/+v4Wj/vXKONZT/mP9v/3b/c/93AG7IhO2OpGjLkfPG6ub/1fejQ3G+etdz/3b/dv+S/4bM" +
            "XwB9u3z/af/j87rJkqix/2z/c/9vg2DjeP+J/5T/fIJt/3Pxf8xr/3jcjf+T+mn/dv9x/3Htcv973XKGaolZAHPtcv+E/2f/bP+D/3T/bf9v/3TxejrmqtcIbQAtAC0A" +
            "avR0/3T/bv+d/3nIc+h2/3D/beR6/5y5fNp+/4+ncAV3AGegZ/92/5illgCSW23Uc/9u/4P/eP96/3X/fN5oBy0Af0SJ/23/ZYQtAJNNhYlq/3P/c/9r/2zPZBYtAC0A" +
            "eQCEAG57c/+OtGZlcf9p/2j/b/9x/2a+Xz8tAOYALQAtAC0ALQBskHf/af9sdnH/ef9k/23/aAotAEgALQAtAC0A5gAtAC0ALQAtAIwAiP92qi0ALQAtAC0ALQAtAC0A" +
            "2wAtAH4oia55pV0AmQB//3j/b6JmnWAPYAAtAOYALQAtAI0Ahaet/5j/b7YtAF/Bgv98/2f/buFw/2ctLQA0AGEAeP58/3X/ff9//4iG5jmZ1Hn/dv96/3b/ZcEtAC0A" +
            "tJt38Xzqcf9+/2vze//Ur4aWef92/2v/av93/3n+aCWt/5SMevB4/3j/atRzlmjLe4pv9Wv/cP9t4W//d692AHf/eRa5HJL/mf9yy3f7eP90/3D/c/9sz5V8cv9tpFIA" +
            "p/985+YS5qh8dnZ2bf9s/23/b/91/4yKpihvVWzwcsm//5re5gC+UOZ52iu1ia3/d/9y/3ngcpgtAC0AlHmN/4zjcyZ9q3L/f9pn/8v/z/6Rspr/e/+O576Cafd0/3K1" +
            "ef93/3n/dbZyu2n/0+7L2b6+l/9w/4P/f/9x/3D/caFy1nKNakRo7Wz/df9k/5L15v+m02H/cP9x/2X/dP+Ha3//hlJnEX7NevOH0oP8jZBj/3H/cfqL8IH/cf9kMi0A" +
            "f5iYAHkAf0uLzHb/gf92ln9Ae/9+/3XMdv91/3AAegAtAC0ALQBvH3v/cP9x/2z4i6CM7nVxcP9x52U1NAAtADQA5gAtAIVDgf90/2zrev+V8Hn/bBWIAHZKLQAtAC0A" +
            "LQAtAFkArgAtAHYAd39yEnEseLUtAFkAlADPAC0ALQAtAC0ALQAtAKMAdrKK/3r/eP96/3VVLQAtAC0ALQAtAM8A5gB0iXhRd8l4/3r/cv9u/33/gf94dWgAdhhZAC0A" +
            "LQB5AHr/h/9//2b/ZP9j/2j/cP90/3//d/96/3gUogB4AHe/f/92/3X/Yf9a/13/Xv9p/3D/dv9//3r/duR5DXRXhP91/3j/a/9W/1n/af9t/23/af93/3P/d/9//3hx" +
            "cABz84L/cP9c/3f/ef96/3z/e/+D/33/eP94/33/dFJ2AHj/ff9t/1n/fP9u/23/ev+L/5H/jv96/4H/eO5xFmsAc4J7/23/ZP95/3r/dP+C/4v/mv+d/4f/f/9z1Hz6" +
            "eQBwZoL/af9y/4n/gv+B/4z/l/+f/57/kP9//4L/eph4iXr/eP9z/3r/k/+c/47/lf+l/7v/ov99/4n/dquFAHMOev94/3j/jP+W/6L/o/+u/7X/vf+v/4z/kf93v2sA" +
            "cgt6/4L/df9+/3//mv+3/8D/yP+//5f/ff96/3jmcAAtAHVkgv94/3P/hf+g/6X/mP+g/5L/j/930npEMQAtAKIALQByX4X/gf94sX3qiP+J/33/g6KE8HVALQAtAC0A" +
            "LQAtAM8AdKd6tS0Aexl8rHthjjItAFkALQBZAC0ALQAtAC0ALQAtAHmXeZRxR3Rsev95nnUALQAtAC0ALQAtAC0ALQAtAHQSeKp7/3r/fP+L/3f/dMt+4XkU0gDSAC0A" +
            "gwBxInncd/928HX/hv+G/4v/kP9+/5z/hWgtAKQALQBuAHbrhP93/4T/gP+R/4n/m/+6/7T/i/+B/4qELQDmAHdOfP9+/3f/ev+D/5r/ov+6/7j/yv++/4v/iP94NS0A" +
            "d8yE/3f/df99/4//n/+Y/6T/rf+0/8D/n/9+/3n/igBnAHT1gf90/3n/iv+O/4//kv+Z/6T/tf+2/6H/d8EtAGcAd/uA/2z/aP96/4X/f/+G/4n/lf+n/5z/mf932nEm" +
            "dBJ7/3L/a/9q/33/ef9z/3n/fP+Z/6H/jP9y/4T/fP92joP/dP9s/1n/bf9+/2j/cf+I/43/kv+Q/37/c/95/3n/d/9s/2L/Yf9b/2r/ff99/3j/ff95/3//ef96/3j/" +
            "ZAB204P/bP9g/1b/Vv9z/2j/Xf9p/2z/d/93/3z/dP4tAHeOkf98/2X/Zf9i/2H/Wv9s/3D/hP9+/3f/e/9yMC0AeSl+53f3gf98/3f/d/95/3f/ev90wXfkefhyLI4A" +
            "LQAtAC0AeQB0jXf/fP93/3f/fv9+/3ABigB5PC0ALQAtAOYA0gAtAGcAef95/3f/fP9znXYVeQAtAC0AWgAtANIA5gByAHn/e/95/3b/fv95/HnMdTbSAKQALQAtAC0A" +
            "LQAtAC0AefWD/3f/df93/3n/ef9nAC0ALQAxAOYALQAtAIMAdit09X//d/+A/3f/dv98/3KHfKt4ES0ALQAtAHoAefp8/5P/fv+H/5D/d/9s/3X/f/+O/3T6dWx2FS0A" +
            "fnV+/47/r/+c/5n/lf9//2//Z/90/3f/ev+Z/3iq0gCI/4P/of+8/6f/of+Y/43/c/9c/1//dP93/4H/dsUtAHvrnP/E/7r/pP+S/4b/gP+A/3b/Wv9s/3X/ev96/3Jc" +
            "hv+c/87/sP+h/4n/gf9x/2n/gP9x/17/dv95/37/dpN5/63/w/+2/6H/j/9//3X/b/92/1j/V/9v/4n/c4wtAHX/n/+w/73/nv+Q/4b/ff94/3j/Wf9b/17/ef91cy0A" +
            "g/+L/4v/ov+e/5j/jf+A/3//Yf9e/2L/b/+B/3aopACG/4b/g/+R/5z/m/+K/3j/bv9j/2f/Yv9x/4b/eP9/AHBvef+G/4j/gP+E/4L/cv9l/27/b/9r/3r/evp2Iy0A" +
            "dgd3/3b/df9+/3v/df92/3n/b/97/4n/dv9qEi0A0gCkAHnQdt522oP/df95/3b/fP95/3n/dNR75IEr0gBaAC0Afyt3QHSXev95/3n/fv9383Q9bwAtAFoAjgAtAC0A" +
            "LQAtAC0ALQAtAEkALQBzAHS8cW90Wm4KLQAtAC0ALQAtAC0ALQAtAC0ALQBkAHM9d/+K/3n/d/FnAG48LQCeAC0ALQAtAKoALQB2ln//dP93/3//cv9z/3b/d/9nAC0A" +
            "LQCqAC0AcwBr6Hb/fP9w/3n/c/9x/3j/e/93/3nqdSstAOYAZgBwhHr/b/9t/2//eP9t/2f/eP9v/3T/dv99AC0A5gBNAHL6hf93/3n/gv9z/3//cP9v/33/e/92/3t/" +
            "hQCFAHEAcP96/3n/jf95y27Ge/93/33/jv92/33/fqotAC0AcjN9/3H/cv+A/4GoXbh17nX/jv97qHsDiAAtAHUOeYp55HX/dP9z/3T/eM1n/3X/ev+I/3+UcQ4tAEkA" +
            "ef+P/4H/cP9+/3v/eP95/3D+avGC/5v/mf97/30YLQB2b4L/cP9w/3n/dv9//3//gf96/37/qv+j/5P/gv98r24NdP+B/3b/gf97/3j/g/+F/5X/tP+t/5P/n/+O/38r" +
            "az10zHjNhv+Q/4H/gf+Q/4P/mf+7/6D/nv+u/4f/bwNnAC0AaAB58In/gDqG/4ZSidSR/6T/n/+s/57LgUBJAC0ASQAtAIYKogEtAHEAqgDmAHfapP+//6H/5gAtAC0A" +
            "LQAtAOYALQAtAKoALQCqAC0Ae3iea88mtgCqAKoALQAtAC0ALQCfAKsASQB84ZH/iv+X/6hfuHF7AKsA5gAtAIYAhgB+CC0AhgCrAHq7o/+U/5//qv+7/8SALQAtAC0A" +
            "LQB+AH//fJEtAHQRgP+c/5f/m/+S/67/rP+STo8A5gBzjnm7df+A/3ctZwB+/5j/rv+z/7L/oP+I/3zMgQCrAHP/g/94/37/e/9+w4T/n/+J/6D/tv+f/4PUawAtAC0A" +
            "df9u/3L/e/+C/5P/gv98/3P/g/+G/4n/i7stAC0ALQBx/3r/d/9w/3D/d/9v/3j/b+B8/4D/k/+N1oKWcgAtAHL/ef9x/2n/e/98/3Loav9q/3v/f/99/5j/f+stAC0A" +
            "c/9u/23/Zf95/3b/ZIJnnXnWdv+E/3P/hP+C1J5XqABz/33/e/99/3T/df950ortdv9x/3f/d/+F/5n/kv+GX3PUi/94/3D/bv92/5L/fv9z/3P/gv95/3b/h/9x7k0A" +
            "caVyiXfNgP9w/27/d/93/3H/cf9y/3H/bv+F/3mnLQBUAC0AbTx7/4X/cv91/3z/gP98/3X/dP90/3X/d/9zWy0ASQAtAG0ebJFr/3T/c/91zHOOdHKG/4T/df9oJm8A" +
            "LQAtAEkALQAtAG4PZg9kJS0ALQAtAHlxhf9zCy0ALQAtAC0ALQBJAOYALQAtAC0ASQBJAC0ALQBuAC0AqwAtAC0ALQAtAC0AgwFwDXUvdup4/3S5gmJ9Ay0ArQAtAC0A" +
            "LQAtAIgASgCSv4L/d/99/3L/gf+W/3tKLQDmAC0ALQAtAC0ALQAtAIuMoP+G/3H/cP98/3n/fAAtAK0ALQAtAJEA5gB6FIvNeuOS/4D/dv9w/4D/ebwtAEoALQAtAC0A" +
            "LQAtAHcAi/+Q/3b/d/+B/3n/dP93/3NVLQDmAC0ALQB1jnxQkNqR/4z/fP96/3j/eP9w/33/cP9qB10ALQAtAJT/jv+U/4f/gf+E/4j/b/91/3j/df92/3n/agctAEoA" +
            "ov+t/6r/jP+D/4D/cvN/9H7/d/97/3z/eP9pfeYA5gCS/7T/wP+N/3P/c91q/3OohsuI/37/bf96/2nkrQCtAKX/ov+u/4b/df90/3f/X7Zpc4H/dv9x/4f/cLFKAK0A" +
            "m/+R/67/pP+N/3r/bed/83j/cf9x/2v/h/961EoArQCQ/6v/kf+S/4j/f/94/3n/f/95/3v/cf+C/29XLQCkAJv/e/+G/3W0e2GR/3j/cf9t/2j/fv98/36ydAstAK0A" +
            "h9mEM4MALQB5IYb/ff9x/3D/bP90/3z/h/902n0A5gAtAC0ALQC7AHrIhf99/3b/ev94/3D/hf9163UAbQAtAC0AbQDmAH4Pff90/3b/b/98/3P/ff9343I3LQAtAC0A");

        // Match the tinted photo cards in linear light, including their shadowed ground footprint.
        public static Color Crown(ForestPattern.Stand stand) =>
            Color.Lerp(((Color)Colours[stand.species]).linear, ((Color)Colours[stand.neighbour]).linear, stand.blend)
            * Mathf.Pow(ForestPattern.Tint(stand.shade), 2.2f);

        // Recolour the existing photographs without changing bark or introducing another set of texture assets.
        public static Color PhotoTint(int species)
        {
            Color target = Colours[species], source = PhotoColours[species];
            return new Color(target.r / source.r, target.g / source.g, target.b / source.b);
        }

        // Ten-to-25-metre crowns ride on 40-to-120-metre masses; coarse texels integrate away the small crowns.
        public static Color Surface(ForestPattern.Stand stand, float x, float z, float footprint)
        {
            Color colour = Color.Lerp(Ground, Linear(stand), Mathf.SmoothStep(0f, 1f, stand.cover)).gamma;
            float fine = Mathf.Clamp01((32f - footprint) / 24f);
            float light = ((ForestPattern.Noise(x, z, 13f, 33) - 0.5f) * 24f
                + (ForestPattern.Noise(x + 8f, z - 3f, 23f, 82) - 0.5f) * 8f) * fine
                + (ForestPattern.Noise(x, z, 57f, 83) - 0.5f) * 12f
                + (ForestPattern.Noise(x - 21f, z, 113f, 84) - 0.5f) * 8f;
            colour.r += light / 255f; colour.g += light / 255f; colour.b += light * 0.8f / 255f;
            // Preserve understorey only in glades; ordinary grove rims must fade when their crowns are ready.
            colour.a = stand.wood > 0f ? stand.cover / stand.wood : 1f;
            return colour;
        }

        // Density after Poisson rejection and clustering is measured by the production-source harness.
        public static Color Linear(ForestPattern.Stand stand)
        {
            float coverage = 1f - Mathf.Exp(-Mathf.PI * stand.radius * stand.radius * 0.0277f);
            return Color.Lerp(Ground, Crown(stand), coverage) * 1.06f;
        }

        // Bilinear filtering prevents nearest-texel speckle when a small crown crosses a baked pixel.
        public static float Picture(int species, int variant, float u, float v, out float alpha)
        {
            if (u < 0f || v < 0f || u > 1f || v > 1f) { alpha = 0f; return 1f; }
            float x = u * 15f, z = v * 15f;
            int i = Mathf.Min(14, (int)x), j = Mathf.Min(14, (int)z), k = ((species * 3 + variant) * 256 + j * 16 + i) * 2;
            float fx = x - i, fz = z - j;
            alpha = Mathf.Lerp(Mathf.Lerp(Pictures[k + 1], Pictures[k + 3], fx), Mathf.Lerp(Pictures[k + 33], Pictures[k + 35], fx), fz) / 255f;
            return Mathf.Lerp(Mathf.Lerp(Pictures[k], Pictures[k + 2], fx), Mathf.Lerp(Pictures[k + 32], Pictures[k + 34], fx), fz) / 128f;
        }
    }
}
