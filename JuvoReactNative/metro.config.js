/**
 * Metro configuration for React Native
 * https://github.com/facebook/react-native
 *
 * @format
 */
const path = require('path');
const exclusionList = require('metro-config/src/defaults/exclusionList');

module.exports = {
    resolver: {
        blockList: exclusionList([
            // This stops "react-native-tizen" from causing the metro server to crash if its already running
            new RegExp(
                `${path.resolve(__dirname, 'tizen').replace(/[/\\]/g, '/')}.*`,
            ),
            // This prevents "react-native-tizen" from hitting: EBUSY: resource busy or locked, open msbuild.ProjectImports.zip
            /.*\.ProjectImports\.zip/,
        ]),
        extraNodeModules: {
            'react-native-tizen': 'C:\\extern\\workbench\\iptv\\archives\\tizen-samples\\react-native-tizen-dotnet\\Devtools\\react-native-tizen-dotnet'
        },
    },
    transformer: {
        getTransformOptions: async () => ({
            transform: {
                experimentalImportSupport: false,
                inlineRequires: true,
            },
        }),
    },
};
