#!/bin/sh

#Set to 1 to generate the certificates.
CREATE_CERT=""

MAKECERT="$(realpath ./.msix/makecert.exe)"
MAKEMSIX="$(realpath ./.msix/makemsix.exe)"
PVK2PFX="$(realpath ./.msix/pvk2pfx.exe)"
SIGNTOOL="$(realpath ./.msix/signtool.exe)"

RAIMUSOFT_CER="$(realpath ./.msix/RaimuSoft.cer)"
RAIMUSOFT_PFX="$(realpath ./.msix/RaimuSoft.pfx)"

PLATFORM="
x86
"

TARGET="
net461
"

if [ -z "$1" ]
then
	version=$(git describe --abbrev=0 --tags)
else
	version="$(date +%F)-nightly"
fi

echo "Current version is $version.."
sleep 1
echo "3.."
sleep 1
echo "2.."
sleep 1
echo "1.."
sleep 1

if [ ! -z "$CREATE_CERT" ]
then
	cd "./.msix"
	rm RaimuSoft.*
	#I have no idea what -eku 1.3.6.1.5.5.7.3.3 does.
	"$MAKECERT" -r -h 0 -n "CN=B9825049-B5CA-400A-AC2F-84950A631926" -eku 1.3.6.1.5.5.7.3.3 -pe -sv "RaimuSoft.pvk" "RaimuSoft.cer"
	"$PVK2PFX" -pvk "RaimuSoft.pvk" -spc "RaimuSoft.cer" -pfx "RaimuSoft.pfx"
	cd ..
fi

rm ./release/*.appx
rm ./release/*.cer
cp "$RAIMUSOFT_CER" "./release/SigningCert.cer"

for platform in $PLATFORM
do
	for target in $TARGET
	do

	cd "./release/$platform/$target"

	echo "Creating manifest.."
	cp "../../../FoxTunes.Manifest.xml" "./main/AppxManifest.xml"

	echo "Creating assets.."
	mkdir -p "./main/Assets"
	cp ../../../Assets/* "main/Assets/"

	echo "Creating msix package.."
	"$MAKEMSIX" pack -d "main" -p "FoxTunes-$version-$target-$platform.appx"
	cp "./FoxTunes-$version-$target-$platform.appx" "../../FoxTunes-$version-$target-$platform.appx"

	echo "Signing msix package.."
	cp "$RAIMUSOFT_PFX" "./SigningCert.pfx"
	"$SIGNTOOL" sign //fd SHA256 //a //f SigningCert.pfx "FoxTunes-$version-$target-$platform.appx"
	cp "./FoxTunes-$version-$target-$platform.appx" "../../FoxTunes-$version-$target-$platform-Signed.appx"

	cd ..
	cd ..
	cd ..

	done
	echo
done
echo

echo "All done."