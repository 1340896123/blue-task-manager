// Reveal production native disclosures through real clicks before using controls
// that moved into the Windows command overflow. This never changes app state
// through evaluation and does not introduce compatibility markup in the product.
export async function revealControl(locator) {
  const base=await locator.evaluate(el=>el.tagName==='SUMMARY')?locator.locator('..'):locator;
  for(;;){
    const disclosure=base.locator('xpath=ancestor::details[not(@open) and not(ancestor::details[not(@open)])]');
    if(!await disclosure.count())break;
    await disclosure.locator(':scope > summary').click();
  }
  return locator;
}
export async function clickCommand(page,name,{menu=false}={}) {
  const locator=(menu?page.locator('.command-menu-content'):page).getByRole('button',{name,exact:true,includeHidden:true});
  await revealControl(locator);await locator.click();
}
export async function openProcessFilter(page) {
  const summary=page.locator('.control-popover > summary').filter({hasText:'筛选'});
  await revealControl(summary);if(!await summary.locator('..').evaluate(el=>el.open))await summary.click();
}
export const resourceTiles=page=>page.locator('.resource-list .resource-tile');
export const resourceTile=(page,type)=>resourceTiles(page).filter({has:page.locator('strong').filter({hasText:new RegExp(`^${type}$`)})});
