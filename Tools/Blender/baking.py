"""Shared helpers for the scripts that bake a model's look into its own textures with Cycles (make_wrecks.py,
make_weathered.py): a small shader-graph builder, the GPU switch, and reading the imported models' materials."""
import bpy


def base_name(mat):
    return mat.name.split(".")[0] if mat else ""


def base_colour(mat):
    if mat and mat.node_tree:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            return tuple(bsdf.inputs["Base Color"].default_value[:3])
    return (0.1, 0.1, 0.1)


class Graph:
    """A small helper for building shader node graphs from Python."""

    def __init__(self, mat):
        self.nt = mat.node_tree
        self.nt.nodes.clear()

    def node(self, kind, **inputs):
        n = self.nt.nodes.new(kind)
        for key, value in inputs.items():
            self.set(n.inputs[key], value)
        return n

    def set(self, socket, value):
        if isinstance(value, bpy.types.NodeSocket):
            self.nt.links.new(value, socket)
        else:
            socket.default_value = value

    def math(self, op, a, b=0.0, clamp=False):
        n = self.node("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        self.set(n.inputs[0], a)
        self.set(n.inputs[1], b)
        return n.outputs[0]

    def vec(self, op, a, b=(0.0, 0.0, 0.0), scale=1.0):
        """Vector math; DOT_PRODUCT and LENGTH give a value, the rest a vector."""
        n = self.node("ShaderNodeVectorMath")
        n.operation = op
        self.set(n.inputs[0], a)
        self.set(n.inputs[1], b)
        if op == "SCALE":
            self.set(n.inputs[3], scale)  # (an unused socket can't be looked up by name)
        return n.outputs["Value"] if op in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else n.outputs["Vector"]

    def smooth(self, x, a, b):
        n = self.node("ShaderNodeMapRange")
        n.interpolation_type = "SMOOTHSTEP"
        self.set(n.inputs["Value"], x)
        self.set(n.inputs["From Min"], a)
        self.set(n.inputs["From Max"], b)
        return n.outputs["Result"]

    def mix(self, a, b, fac):
        n = self.node("ShaderNodeMix")
        n.data_type = "RGBA"
        self.set(n.inputs["Factor"], fac)
        self.set(n.inputs[6], a)  # A (colour)
        self.set(n.inputs[7], b)  # B (colour)
        return n.outputs[2]

    def multiply(self, a, b):
        n = self.node("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = "MULTIPLY"
        n.inputs["Factor"].default_value = 1.0
        self.set(n.inputs[6], a)
        self.set(n.inputs[7], b)
        return n.outputs[2]

    def noise(self, vector, scale, detail=4.0, roughness=0.55):
        n = self.node("ShaderNodeTexNoise", Vector=vector, Scale=scale, Detail=detail, Roughness=roughness)
        return n.outputs["Fac"]


def use_gpu():
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
        except TypeError:
            continue
        prefs.get_devices()
        devices = [d for d in prefs.devices if d.type == kind]
        if devices:
            for d in prefs.devices:
                d.use = d.type == kind
            bpy.context.scene.cycles.device = "GPU"
            return kind
    return "CPU"
