import os
import shutil

os.chdir(os.path.expanduser("~"))

src_path = os.path.join("Documents", "workspace", "microting", "eform-service-backendconfiguration-plugin", "ServiceBackendConfigurationPlugin")
dst_base = os.path.join("Documents", "workspace", "microting", "eform-debian-service", "Plugins")
dst_path = os.path.join(dst_base, "ServiceBackendConfigurationPlugin")

if os.path.exists(dst_path):
    shutil.rmtree(dst_path)

os.makedirs(dst_base, exist_ok=True)

shutil.copytree(src_path, dst_path)
