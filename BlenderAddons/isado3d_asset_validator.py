bl_info = {
    "name": "Isa do 3D - Asset Validator",
    "author": "Isa",
    "version": (1, 0, 0),
    "blender": (3, 4, 0),
    "location": "View3D > Sidebar > Isa do 3D > Validate Asset",
    "description": "Checks modular conventions (applied scale, pivot/contact points on grid, UV, n-gons, materials) before exporting to the engine. Some issues can be fixed in one click.",
    "category": "Object",
}

import bpy
import re
from mathutils import Vector
from bpy.types import Operator, Panel, AddonPreferences
from bpy.props import BoolProperty, FloatProperty, IntProperty

ADDON_ID = __name__

ERROR = 'ERROR'
WARN = 'WARN'
INFO = 'INFO'

_SEVERITY_ORDER = {ERROR: 0, WARN: 1, INFO: 2}
_SEVERITY_ICON = {ERROR: 'CANCEL', WARN: 'ERROR', INFO: 'INFO'}

_last_results = []
_last_summary = ""

_LOD_RE = re.compile(r'_LOD\d+$')


class IsaDo3DValidatorPreferences(AddonPreferences):
    bl_idname = ADDON_ID

    grid_unit: FloatProperty(
        name="Grid unit",
        description="Grid increment used to check pivot and contact points (use a power of 2: 1, 2, 4...)",
        default=1.0,
        min=0.001,
        soft_max=8.0,
    )

    tolerance: FloatProperty(
        name="Tolerance",
        description="Slack allowed when comparing positions against the grid and transforms against the expected value",
        default=0.001,
        min=0.0,
        soft_max=0.1,
        precision=4,
    )

    max_materials: IntProperty(
        name="Max materials",
        description="A modular piece should reuse few materials (ideally 1 shared trim/atlas)",
        default=1,
        min=1,
        soft_max=8,
    )

    check_scale: BoolProperty(name="Scale applied", default=True)
    check_rotation: BoolProperty(name="Rotation applied", default=True)
    check_pivot_grid: BoolProperty(name="Pivot on grid", default=True)
    check_bounds_grid: BoolProperty(name="Contact points on grid", default=True)
    check_uv: BoolProperty(name="Has UV", default=True)
    check_uv_bounds: BoolProperty(
        name="UV within 0-1",
        description="Off by default: tiling walls deliberately go outside 0-1. Turn on for a trim sheet workflow.",
        default=False,
    )
    check_ngons: BoolProperty(name="No n-gons", default=True)
    check_materials: BoolProperty(name="Material count", default=True)
    check_lod_naming: BoolProperty(
        name="LOD naming (_LOD0/1/2)",
        description="Warns when the name mentions LOD but does not end in _LOD followed by a number",
        default=True,
    )

    def draw(self, context):
        layout = self.layout
        col = layout.column(align=True)
        col.prop(self, "grid_unit")
        col.prop(self, "tolerance")
        col.prop(self, "max_materials")

        layout.separator()
        layout.label(text="Active rules:")
        grid = layout.grid_flow(row_major=True, columns=2, even_columns=True)
        grid.prop(self, "check_scale")
        grid.prop(self, "check_rotation")
        grid.prop(self, "check_pivot_grid")
        grid.prop(self, "check_bounds_grid")
        grid.prop(self, "check_uv")
        grid.prop(self, "check_uv_bounds")
        grid.prop(self, "check_ngons")
        grid.prop(self, "check_materials")
        grid.prop(self, "check_lod_naming")


def _get_prefs(context):
    return context.preferences.addons[ADDON_ID].preferences


def _off_grid(value, unit, tol):
    nearest = round(value / unit) * unit
    return abs(value - nearest) > tol


def _world_bbox_corners(obj):
    mw = obj.matrix_world
    return [mw @ Vector(corner) for corner in obj.bound_box]


def _validate_object(obj, prefs):
    issues = []
    unit = prefs.grid_unit
    tol = prefs.tolerance

    if prefs.check_scale:
        sx, sy, sz = obj.scale
        if any(abs(s - 1.0) > tol for s in (sx, sy, sz)):
            issues.append((ERROR, f"Scale not applied ({sx:.3g}, {sy:.3g}, {sz:.3g})"))

    if prefs.check_rotation:
        if any(abs(r) > tol for r in obj.rotation_euler):
            issues.append((WARN, "Rotation not applied"))

    if prefs.check_pivot_grid:
        loc = obj.matrix_world.translation
        if any(_off_grid(c, unit, tol) for c in loc):
            issues.append((WARN, f"Pivot off grid ({loc.x:.3g}, {loc.y:.3g}, {loc.z:.3g})"))

    if prefs.check_bounds_grid:
        corners = _world_bbox_corners(obj)
        off = any(_off_grid(c, unit, tol) for corner in corners for c in corner)
        if off:
            issues.append((WARN, "Contact points off grid - the piece may not tile"))

    mesh = obj.data

    if prefs.check_uv and len(mesh.uv_layers) == 0:
        issues.append((ERROR, "No UV map"))

    if prefs.check_uv_bounds and len(mesh.uv_layers) > 0:
        uv = mesh.uv_layers.active.data
        out = any(
            (d.uv.x < -tol or d.uv.x > 1.0 + tol or d.uv.y < -tol or d.uv.y > 1.0 + tol)
            for d in uv
        )
        if out:
            issues.append((WARN, "UV outside 0-1 (fine if tiling; heads-up for trim sheet workflow)"))

    if prefs.check_ngons:
        ngons = sum(1 for p in mesh.polygons if len(p.vertices) > 4)
        if ngons > 0:
            issues.append((WARN, f"{ngons} n-gon(s) - may cause bad shading in the engine"))

    if prefs.check_materials:
        slots = len([s for s in obj.material_slots if s.material is not None])
        if slots > prefs.max_materials:
            issues.append((WARN, f"{slots} materials (expected <= {prefs.max_materials})"))
        if slots == 0:
            issues.append((INFO, "No material assigned"))

    if prefs.check_lod_naming and 'lod' in obj.name.lower():
        if not _LOD_RE.search(obj.name):
            issues.append((WARN, "Name mentions LOD but doesn't end in _LOD0/1/2"))

    return issues


def _target_objects(context):
    selected = [o for o in context.selected_objects if o.type == 'MESH']
    if selected:
        return selected
    return [o for o in context.scene.objects if o.type == 'MESH']


class ISADO3D_OT_validate_assets(Operator):
    bl_idname = "isado3d.validate_assets"
    bl_label = "Validate Asset"
    bl_description = ("Checks the selected meshes (or all of them, if nothing is selected) "
                      "against the export conventions. Changes nothing.")
    bl_options = {'REGISTER'}

    def execute(self, context):
        global _last_results, _last_summary
        prefs = _get_prefs(context)

        objects = _target_objects(context)
        if not objects:
            _last_results = []
            _last_summary = "No mesh in the scene."
            self.report({'WARNING'}, _last_summary)
            return {'CANCELLED'}

        _last_results = []
        n_err = n_warn = n_info = 0
        for obj in objects:
            issues = _validate_object(obj, prefs)
            if issues:
                issues.sort(key=lambda i: _SEVERITY_ORDER[i[0]])
                _last_results.append({"name": obj.name, "issues": issues})
                for sev, _ in issues:
                    if sev == ERROR:
                        n_err += 1
                    elif sev == WARN:
                        n_warn += 1
                    else:
                        n_info += 1

        clean = len(objects) - len(_last_results)
        _last_summary = (f"{len(objects)} mesh(es): {clean} ok, "
                         f"{n_err} error(s), {n_warn} warning(s), {n_info} info.")

        if n_err:
            self.report({'ERROR'}, _last_summary)
        elif n_warn:
            self.report({'WARNING'}, _last_summary)
        else:
            self.report({'INFO'}, "All good! " + _last_summary)
        return {'FINISHED'}


class ISADO3D_OT_select_failed(Operator):
    bl_idname = "isado3d.select_failed"
    bl_label = "Select flagged"
    bl_description = "Selects in the viewport every object flagged in the last validation"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return bool(_last_results)

    def execute(self, context):
        # Selecting needs Object Mode
        if context.mode != 'OBJECT':
            if context.object is None:
                self.report({'WARNING'}, "Leave edit mode to select objects.")
                return {'CANCELLED'}
            bpy.ops.object.mode_set(mode='OBJECT')

        names = {r["name"] for r in _last_results}
        first = None
        # select_all fails out of context
        for obj in context.view_layer.objects:
            hit = obj.name in names
            obj.select_set(hit)
            if hit and first is None:
                first = obj
        if first:
            context.view_layer.objects.active = first
        self.report({'INFO'}, f"{len(names)} object(s) selected.")
        return {'FINISHED'}


class ISADO3D_OT_fix_transforms(Operator):
    bl_idname = "isado3d.fix_transforms"
    bl_label = "Apply scale/rotation"
    bl_description = ("Applies rotation and scale on the selected objects (transform_apply). "
                      "Fixes the most common export issue. Re-validate afterwards.")
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return (context.mode == 'OBJECT'
                and any(o.type == 'MESH' for o in context.selected_objects))

    def execute(self, context):
        meshes = [o for o in context.selected_objects if o.type == 'MESH']
        try:
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        except RuntimeError as e:
            self.report({'ERROR'}, f"Failed to apply transform: {e}")
            return {'CANCELLED'}
        self.report({'INFO'}, f"Scale/rotation applied on {len(meshes)} object(s).")
        return {'FINISHED'}


class ISADO3D_OT_snap_pivot_grid(Operator):
    bl_idname = "isado3d.snap_pivot_grid"
    bl_label = "Snap position to grid"
    bl_description = ("Rounds the selected objects' position to the nearest grid point. "
                      "Moves the object onto the grid; does not change the internal geometry.")
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return (context.mode == 'OBJECT'
                and any(o.type == 'MESH' for o in context.selected_objects))

    def execute(self, context):
        unit = _get_prefs(context).grid_unit
        meshes = [o for o in context.selected_objects if o.type == 'MESH']
        for obj in meshes:
            loc = obj.location
            obj.location = Vector((round(loc.x / unit) * unit,
                                   round(loc.y / unit) * unit,
                                   round(loc.z / unit) * unit))
        self.report({'INFO'}, f"Position snapped to grid ({unit}) on {len(meshes)} object(s).")
        return {'FINISHED'}


class ISADO3D_PT_asset_validator(Panel):
    bl_label = "Validate Asset"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Isa do 3D"

    def draw(self, context):
        layout = self.layout

        n_sel = len([o for o in context.selected_objects if o.type == 'MESH'])
        target = f"{n_sel} selected" if n_sel else "all meshes"
        layout.label(text=f"Target: {target}", icon='RESTRICT_SELECT_OFF')

        layout.operator(ISADO3D_OT_validate_assets.bl_idname, icon='CHECKMARK')

        row = layout.row(align=True)
        row.operator(ISADO3D_OT_fix_transforms.bl_idname, text="Apply T", icon='OBJECT_ORIGIN')
        row.operator(ISADO3D_OT_snap_pivot_grid.bl_idname, text="Grid", icon='SNAP_GRID')

        if _last_summary:
            layout.separator()
            layout.label(text=_last_summary)

        if _last_results:
            layout.operator(ISADO3D_OT_select_failed.bl_idname, icon='RESTRICT_SELECT_OFF')
            box = layout.box()
            for result in _last_results:
                col = box.column(align=True)
                col.label(text=result["name"], icon='OBJECT_DATA')
                for severity, message in result["issues"]:
                    row = col.row()
                    row.label(text=message, icon=_SEVERITY_ICON[severity])


classes = (
    IsaDo3DValidatorPreferences,
    ISADO3D_OT_validate_assets,
    ISADO3D_OT_select_failed,
    ISADO3D_OT_fix_transforms,
    ISADO3D_OT_snap_pivot_grid,
    ISADO3D_PT_asset_validator,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)


def unregister():
    global _last_results, _last_summary
    _last_results = []
    _last_summary = ""
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
