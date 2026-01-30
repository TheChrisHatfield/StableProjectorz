# Add-on Installation System

StableProjectorz now includes a user-friendly add-on installation system similar to Blender's add-on manager.

## Features

### ✅ Drag-and-Drop Installation
- Simply drag a `.zip` file containing an add-on into the StableProjectorz window
- The add-on will be automatically extracted and installed
- Validates that the zip contains a `__init__.py` file

### ✅ File Browser Installation
- Use the "Install from File" button in the Add-on Manager panel
- Browse and select a zip file to install
- Shows installation progress and status

### ✅ Add-on Management Panel
- View all installed add-ons
- Enable/disable add-ons with a toggle
- Remove add-ons with confirmation dialog
- Refresh the add-on list

## How It Works

### Installation Process

1. **Zip File Structure**
   - The zip file should contain an add-on directory with `__init__.py`
   - Can be structured as:
     ```
     MyAddon.zip
     └── MyAddon/
         ├── __init__.py
         └── other_files.py
     ```
   - Or directly:
     ```
     MyAddon.zip
     ├── __init__.py
     └── other_files.py
     ```

2. **Extraction**
   - Zip is extracted to a temporary directory
   - System finds the root directory containing `__init__.py`
   - Add-on ID is determined from directory name or `__init__.py` metadata

3. **Installation**
   - Add-on is copied to `StreamingAssets/Addons/{addonId}/`
   - If add-on already exists, it's backed up before overwriting
   - Add-on discovery is triggered automatically

4. **Validation**
   - Verifies `__init__.py` exists after installation
   - Shows success/error messages

### Removal Process

1. User clicks "Remove" button for an add-on
2. Confirmation dialog appears
3. Add-on is unloaded if currently active
4. Directory is deleted
5. Add-on list is refreshed

## Components

### AddonInstaller_MGR.cs
- Handles zip extraction and installation
- Validates add-on structure
- Manages add-on removal
- Located in `Assets/_gm/Features/AddonSystem/`

### AddonManager_UI.cs
- UI panel for managing add-ons
- Provides install, enable/disable, and remove functionality
- Shows add-on list with status
- Located in `Assets/_gm/Features/AddonSystem/`

### FileDragAndDrop.cs (Extended)
- Now handles `.zip` files
- Automatically triggers installation when zip is dropped
- Located in `Assets/UnityWindowsFileDrag-Drop/`

## Usage

### For Users

**Method 1: Drag-and-Drop**
1. Create a zip file of your add-on
2. Drag it into the StableProjectorz window
3. Wait for installation confirmation

**Method 2: File Browser**
1. Open Add-on Manager (via Settings or menu)
2. Click "Install from File"
3. Select the zip file
4. Wait for installation confirmation

**Managing Add-ons**
1. Open Add-on Manager panel
2. View list of installed add-ons
3. Toggle enable/disable for each add-on
4. Click "Remove" to uninstall (with confirmation)

### For Developers

**Creating Installable Add-ons**

1. Create your add-on directory:
   ```
   MyAddon/
   ├── __init__.py
   └── your_code.py
   ```

2. Create `__init__.py`:
   ```python
   import spz
   
   def register():
       api = spz.get_api()
       # Your add-on initialization code
       print("MyAddon registered!")
   ```

3. Zip the directory:
   - Zip the `MyAddon` folder (not its contents)
   - Name it `MyAddon.zip`

4. Distribute the zip file

**Add-on ID**
- Default: Directory name from zip
- Can be specified in `__init__.py` via metadata (future enhancement)

## Technical Details

### Installation Location
- All add-ons are installed to: `StreamingAssets/Addons/{addonId}/`
- This is the same location where manually placed add-ons are discovered

### Backup System
- When overwriting an existing add-on, a backup is created
- Backup format: `{addonId}_backup_{timestamp}`
- Backups are not automatically cleaned up (user can delete manually)

### Error Handling
- Invalid zip files show error messages
- Missing `__init__.py` is caught and reported
- Installation failures are logged and displayed to user

## Future Enhancements

- [ ] Add-on metadata extraction (name, version, description)
- [ ] Automatic backup cleanup
- [ ] Add-on update system
- [ ] Remote add-on repository support
- [ ] Add-on dependencies management
- [ ] Installation from URL

## Notes

- The Add-on Manager UI panel needs to be created in Unity Editor
- Connect the UI elements to `AddonManager_UI` component
- The panel can be integrated into Settings UI or as a standalone window
