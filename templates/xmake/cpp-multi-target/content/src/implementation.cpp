module {{id:projectName}};
{{#internalPartition}}import :detail;
{{/internalPartition}}int answer() {
    int increment = 2;
{{#internalPartition}}    increment = offset();
{{/internalPartition}}    return base + increment;
}
