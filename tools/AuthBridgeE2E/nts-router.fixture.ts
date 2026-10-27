import {useEffect,useState} from 'react';
export const router={replace(path:string){history.replaceState(null,'',path);window.dispatchEvent(new Event('fixture-route'));}};
export function usePathname(){const [path,setPath]=useState(location.pathname);useEffect(()=>{const update=()=>setPath(location.pathname);window.addEventListener('fixture-route',update);return()=>window.removeEventListener('fixture-route',update);},[]);return path;}
