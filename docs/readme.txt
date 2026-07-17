Simple Port Forwarder (SPF)
==========================

SPF is a lightweight, portable Windows utility to manage and run SSH port forwarding tunnels.

📁 Files in this package:
-----------------------
1. SPF.exe          - The main executable.
2. tunnels.json     - The configuration file containing sample tunnels.
3. readme.txt       - This instruction file.

🚀 Getting Started:
-----------------
1. Place SPF.exe and tunnels.json in the same folder.
2. Double-click SPF.exe to run the application (it will automatically load the sample tunnels from the tunnels.json file).
3. If Windows displays a "SmartScreen / Unknown Publisher" warning:
   - Right-click SPF.exe -> Select "Properties".
   - Check the "Unblock" box at the bottom -> Click OK.
   - Run the application again.
4. Click "New Tunnel" to add your forwarding configurations (Local, Remote, or Dynamic).
5. For password authentication, SPF will prompt you for the password at runtime and will NEVER save it to tunnels.json for security.

📝 Configuration & Backup:
------------------------
- Your active tunnel configurations are saved automatically to tunnels.json.
- You can backup your configurations by exporting them from the menu: File -> Export Config.
