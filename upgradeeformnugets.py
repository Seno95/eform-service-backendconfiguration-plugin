import os
import subprocess
import re
import requests
import json

def run_command(command, check=True, capture_output=False):
    return subprocess.run(command, shell=True, check=check, capture_output=capture_output, text=True).stdout.strip()

def get_git_status():
    return run_command("git status | grep 'nothing to commit, working tree clean' | wc -l", capture_output=True)

def get_commit_count():
    return int(run_command("git log --oneline | wc -l", capture_output=True))

def get_package_version(package_name, project_name):
    output = run_command(f"dotnet list {project_name} package | grep '{package_name} '")
    match = re.search(r'(\d+\.\d+\.\d+)', output)
    return match.group(0) if match else None

def bump_package_version(project_name, package_name, old_version, new_version, repository):
    issue_data = {
        "title": f"Bump {package_name} from {old_version} to {new_version}",
        "body": "TBD",
        "assignees": ["renemadsen"],
        "labels": [".NET", "backend", "enhancement"]
    }

    headers = {
        "Authorization": f"token {os.getenv('CHANGELOG_GITHUB_TOKEN')}",
        "Content-Type": "application/json"
    }

    response = requests.post(
        f"https://api.github.com/repos/microting/{repository}/issues",
        headers=headers,
        data=json.dumps(issue_data)
    )

    issue_number = response.json().get("number")
    run_command("git add .")
    run_command(f"git commit -a -m 'closes #{issue_number}'")

def get_git_version():
    return run_command("git tag --sort=-creatordate | head -n 1").replace('v', '')

def update_git_version():
    current_version = get_git_version()
    major, minor, build = map(int, current_version.split('.'))
    new_version = f"v{major}.{minor}.{build + 1}"
    run_command(f"git tag {new_version}")
    run_command("git push --tags")
    run_command("git push")
    return new_version

def process_repository(project_name, packages, repository):
    for package_name in packages:
        old_version = get_package_version(package_name, project_name)
        run_command(f"dotnet add {project_name} package {package_name}")
        new_version = get_package_version(package_name, project_name)

        if new_version and new_version != old_version:
            bump_package_version(project_name, package_name, old_version, new_version, repository)

def main():
    os.chdir(os.path.expanduser("~"))

    if get_git_status() > "0":
        run_command("git checkout master")
        run_command("git pull")
        os.chdir("ServiceBackendConfigurationPlugin")
        
        current_number_of_commits = get_commit_count()
        packages = [
            'Microting.eForm', 'Microting.eFormApi.BasePn', 'Microting.EformBackendConfigurationBase',
            'Microting.ItemsPlanningBase', 'SendGrid', 'ChemicalsBase',
            'Microting.EformAngularFrontendBase', 'Microting.eFormCaseTemplateBase'
        ]
        process_repository('ServiceBackendConfigurationPlugin.csproj', packages, 'eform-service-backend-configuration-plugin')

        new_number_of_commits = get_commit_count()
        if new_number_of_commits > current_number_of_commits:
            new_version = update_git_version()
            print(f"Updated Microting eForm and pushed new version {new_version}")
        else:
            print("Nothing to do, everything is up to date.")
        os.chdir("..")
    else:
        print("Working tree is not clean, so we are not going to upgrade. Clean, before doing upgrade!")

    if get_git_status() > "0":
        run_command("git checkout master")
        run_command("git pull")
        os.chdir("ci/eformparsed")

        current_number_of_commits = get_commit_count()
        packages = [
            'Microting.eForm', 'Microting.EformBackendConfigurationBase', 'Microting.ItemsPlanningBase',
            'Microting.Rebus', 'Microting.Rebus.Castle.Windsor', 'Microting.Rebus.RabbitMq'
        ]
        process_repository('eformparsed.csproj', packages, 'eform-service-backend-configuration-plugin')

        new_number_of_commits = get_commit_count()
        if new_number_of_commits > current_number_of_commits:
            new_version = update_git_version()
            print(f"Updated Microting eForm and pushed new version {new_version}")
            os.chdir("..")
        else:
            print("Nothing to do, everything is up to date.")
    else:
        print("Working tree is not clean, so we are not going to upgrade. Clean, before doing upgrade!")

if __name__ == "__main__":
    main()
