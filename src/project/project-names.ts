export interface ProjectNames {
    displayName: string;
    slug: string;
    namespace: string;
    databaseName: string;
}

export function createProjectNames(input: string): ProjectNames {
    const words = input
        .trim()
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .split(/[^a-zA-Z0-9]+/)
        .filter(Boolean);

    if (words.length === 0) {
        throw new Error("Input string must contain at least one alphanumeric character.");
    }

    const lowerCaseWords = words.map(word => word.toLowerCase());

    const pascalCaseWords = lowerCaseWords.map(
        word => word.charAt(0).toUpperCase() + word.slice(1)
    );

    return {
        displayName: pascalCaseWords.join(" "),
        slug: lowerCaseWords.join("-"),
        namespace: pascalCaseWords.join(""),
        databaseName: lowerCaseWords.join("_"),
    };
}