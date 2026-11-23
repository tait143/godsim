"""AssetForge: GodSim's procedural art pipeline.

Generators build stylized low-poly models with :class:`forge.mesh.MeshBuilder`, colour them with
:mod:`forge.style`, and publish them with :func:`forge.registry.publish`, which validates the mesh, writes a
Unity-ready ``.glb`` (vertex colours, flat shading, Y-up, metres = tiles), renders a PNG preview and records a
manifest entry. See tools/AssetForge/README.md and docs/ART_STYLE_GUIDE.md.
"""
from .mesh import MeshBuilder  # noqa: F401
from . import style  # noqa: F401
from .registry import publish, AssetMeta  # noqa: F401
