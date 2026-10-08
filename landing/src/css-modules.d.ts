// TypeScript paketin içindəki CSS faylının (`import '@splidejs/splide/css/core'`) tipini bilmir və
// redaktorda «Cannot find module» xətası göstərir. Build bundan təsirlənmir — yalnız qırmızı xətt
// çıxır. Bu sətir TypeScript-ə deyir: belə modul var, içindən heç nə import etmirik, tip lazım deyil.
// https://www.typescriptlang.org/docs/handbook/modules/reference.html#ambient-modules
declare module '@splidejs/splide/css/core'
