var through = require('through2');
var path = require('path');
var Set = require('es6-set');
var Map = require('es6-map');

var ravenConfigurations = new Map();
var latestFile;

var HANDLER_PATTERN = "Configuration.cs";

module.exports = function parseConfigurations(outputFileName) {
    return through.obj(function (inputFile, encoding, callback) {
        latestFile = inputFile;
        callback(null, findConfigurationAnnotations(inputFile, ravenConfigurations));
    }, function (cb) {
        var outputFile = createDefinitionFile(ravenConfigurations, outputFileName);
        this.push(outputFile);
        cleanup();
        cb();
    });
};

function cleanup() {
    ravenConfigurations.clear();
    latestFile = null;
}

function findConfigurationAnnotations(file, ravenConfigurations) {
    var contents = file.contents.toString();
    var configurationGroupName = findGroupName(file.path);
    var configKeys = extractSettings(contents);
    if (ravenConfigurations.has(configurationGroupName)) {
        throw new Error("Configuration name clush:" + configurationGroupName);
    }
    ravenConfigurations.set(configurationGroupName, configKeys);
    return null;
}

function findGroupName(input) {
    var fileName = path.basename(input);
    if (!fileName.endsWith(HANDLER_PATTERN)) {
        throw new Error("Cannot handle file: " + input);
    }

    return fileName.substring(0, fileName.length - 16);
}

function extractSettings(contents) {
    // line format: [ConfigurationEntry("Raven/Databases/ConcurrentResourceLoadTimeoutInSec")]
    var annotationRegexp = /(\[ConfigurationEntry\(\"([^"]+)\"\)])|(public\W[\w\?]+\W(\w+))/g;
    var match;
    var matches = new Map();
    var configurationEntry = null;
    
    while ((match = annotationRegexp.exec(contents))) {
        var possibleConfigurationEntry = match[2];
        var possibleFieldName = match[4];

        if (!possibleConfigurationEntry && !possibleFieldName) {
            throw new Error("Invalid match: " + match[0]);
        }

        if (possibleConfigurationEntry) {
            if (configurationEntry) {
                throw new Error("Expected field name. Got another ConfigurationEntry attribute: " + possibleConfigurationEntry + ". Previous: " + configurationEntry);
            }
            configurationEntry = possibleConfigurationEntry;
        } else if (possibleFieldName) {
            if (!configurationEntry) {
                continue;
            }
            matches.set(possibleFieldName, configurationEntry);
            configurationEntry = null;
        }
    }

    return matches;
}


function createDefinitionFile(configurations, outputFileName) {

    var typingSource =
        "// This class is autogenerated. Do NOT modify\n\n" + 
        "class configurationConstants {\n";

    configurations.forEach(function (configMapping, groupName) {
        var groupNameLowerCased = groupName.charAt(0).toLowerCase() + groupName.slice(1);

        typingSource += "    static " + groupNameLowerCased + " = { \n";

        configMapping.forEach(function (configKey, fieldName) {
            var fieldLowerCased = fieldName.charAt(0).toLowerCase() + fieldName.slice(1);
            typingSource += "        " + fieldLowerCased + ": \"" + configKey + "\",\n";
        });

        typingSource += "    }\n";
    });
    typingSource += "\n}";
    typingSource += "\nexport = configurationConstants;";

    var outputFile = latestFile.clone({ contents: false });
    outputFile.path = path.join(latestFile.base, outputFileName);
    outputFile.contents = new Buffer(typingSource);
    return outputFile;
}
