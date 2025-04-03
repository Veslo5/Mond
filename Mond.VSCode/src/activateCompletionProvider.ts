import * as vscode from "vscode";
import { keywords, stlMethods, stlConstants } from "./completionItems"

export function activateCompletionProvider(context: vscode.ExtensionContext) {
    context.subscriptions.push(        
        vscode.languages.registerCompletionItemProvider(
            { language: "mond" },
            {
                provideCompletionItems(
                    document: vscode.TextDocument,
                    position: vscode.Position
                ) {
                    const keywordCompletionItems = keywords.map((keyword) => {
                        const item = new vscode.CompletionItem(
                            keyword,
                            vscode.CompletionItemKind.Keyword
                        );
                        return item;
                    });

                    const config = vscode.workspace.getConfiguration('mond.standardLibraries');
                    const stlEnableCompletion = config.get<boolean>('enableCompletion');
                    
                    if (!stlEnableCompletion) {
                        return [... keywordCompletionItems];
                    }

                    const constantItems = stlConstants.map((method) => {
                        const item = new vscode.CompletionItem(
                            method,
                            vscode.CompletionItemKind.Constant
                        );

                        return item;
                    });

                    const methodItems = stlMethods.map((method) => {
                        const item = new vscode.CompletionItem(
                            method,
                            vscode.CompletionItemKind.Method
                        );

                        return item;
                    });

                    return [...keywordCompletionItems, ...methodItems, ...constantItems];
                },
            }
        )
    );
}