import os
import shutil

os.chdir(os.path.expanduser("~"))

dst_path = os.path.join("Documents", "workspace", "microting", "eform-service-backendconfiguration-plugin", "ServiceBackendConfigurationPlugin")
src_path = os.path.join("Documents", "workspace", "microting", "eform-debian-service", "Plugins", "ServiceBackendConfigurationPlugin")

if os.path.exists(dst_path):
    shutil.rmtree(dst_path)

shutil.copytree(src_path, dst_path)
