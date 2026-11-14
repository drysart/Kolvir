#!/usr/bin/env python3
"""Check that a package carries one lib/ folder and one dependency group per platform, and that
every assembly in it has its XML documentation file beside it.

NuGet selects a lib/ folder and a dependency group by nearest-framework match, so a missing
platform is not an error: a net10.0-windows consumer of a package with no Windows folder compiles
against the net10.0 folder instead and ships the Linux assembly, and a missing dependency group
falls back to the net10.0 one. Both failures are silent at every stage except this one, which is
why the pack job opens every package rather than trusting that `dotnet pack` succeeded.

The documentation file is the same kind of silent failure. `<summary>` comments live in
`lib/<tfm>/<assembly>.xml`, which the SDK writes only when `GenerateDocumentationFile` is set, and
a package without it still installs, still compiles and still runs - it just gives a consumer no
IntelliSense and no doc popups, which is exactly the kind of thing nobody notices until someone
files a bug about it.

The expected shape is in FEATURE-COST-POLICY.md section 4: one folder and one group per platform,
the group holding that platform's whole closure, and an empty group where a platform needs no
extra packages. Every package here is multi-targeted the same way, so the check applies to the
library and the backends as well as to the features.

Usage: check-package-layout.py <package.nupkg>...
Exit code 1 if any package is missing a folder, a group or a documentation file.
"""

import sys
import xml.etree.ElementTree as ET
import zipfile


def local_name(tag):
	"""The element name without its namespace, so the nuspec namespace version does not matter."""
	return tag.rsplit('}', 1)[-1]


def inspect(path):
	"""Return (problems, folders, groups, documented) for one package."""
	try:
		package = zipfile.ZipFile(path)
	except (OSError, zipfile.BadZipFile) as error:
		return [f'cannot be read as a package: {error}'], [], [], []

	with package:
		names = package.namelist()
		nuspec = next((name for name in names if name.lower().endswith('.nuspec')), None)
		if nuspec is None:
			return ['no .nuspec in the package'], [], [], []
		root = ET.fromstring(package.read(nuspec))

	folders = sorted({name.split('/')[1] for name in names
					  if name.startswith('lib/') and name.count('/') >= 2})
	groups = sorted({group.get('targetFramework') or '(any)'
					 for group in root.iter() if local_name(group.tag) == 'group'})

	problems = []
	if not folders:
		problems.append('no lib/ folder, so the package has nothing to compile against')
	for folder in folders:
		if folder not in groups:
			problems.append(f'lib/{folder} has no dependency group of the same name, so a '
							f'{folder} consumer inherits another platform\'s dependencies')
	for group in groups:
		if group not in folders:
			problems.append(f'dependency group {group} has no lib/ folder of the same name, so '
							f'it can only be selected by a platform the package cannot compile '
							f'for')
	for name in names:
		# lib/<tfm>/<assembly>.dll beside lib/<tfm>/<assembly>.xml: an assembly shipped without
		# its documentation file installs and runs, and silently gives up its IntelliSense.
		if not name.startswith('lib/') or not name.lower().endswith('.dll'):
			continue
		documentation = name[:-len('.dll')] + '.xml'
		if documentation not in names:
			problems.append(f'{name} has no documentation file ({documentation}) beside it, so a '
							f'consumer of it gets no IntelliSense and no doc popups')

	documented = sorted({name.split('/')[1] for name in names
						 if name.startswith('lib/') and name.lower().endswith('.xml')})
	return problems, folders, groups, documented


def main(paths):
	if not paths:
		print('error: no packages given')
		return 1

	status = 0
	for path in paths:
		problems, folders, groups, documented = inspect(path)
		print(f'### {path}')
		print()
		print('| platform | lib/ folder | dependency group | documentation |')
		print('| --- | --- | --- | --- |')
		for folder in folders:
			print(f'| {folder} | yes | {"yes" if folder in groups else "**missing**"} | '
				  f'{"yes" if folder in documented else "**missing**"} |')
		for group in groups:
			if group not in folders:
				print(f'| {group} | **missing** | yes | n/a |')
		print()
		for problem in problems:
			print(f'error: {path}: {problem}')
		status |= 1 if problems else 0
	return status


if __name__ == '__main__':
	sys.exit(main(sys.argv[1:]))
